using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NativeBeam.Pdf.Cdp;

/// <summary>
/// AOT-clean DevTools Protocol client. Wraps <see cref="ClientWebSocket"/>,
/// dispatches responses to per-id <see cref="TaskCompletionSource{TResult}"/>
/// completions, and serializes payloads exclusively through the source-generated
/// <see cref="CdpJsonContext"/>. No reflection, no dynamic code.
/// </summary>
internal sealed class CdpConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _ws;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<long, CdpEventSubscription> _subscriptions = new();
    private readonly Task _receiveLoop;
    private int _nextId;
    private long _nextSubscriptionToken;
    private int _disposed;

    private CdpConnection(ClientWebSocket ws)
    {
        _ws = ws;
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    public static async Task<CdpConnection> ConnectAsync(Uri webSocketUri, CancellationToken cancellationToken)
    {
        var ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(webSocketUri, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ws.Dispose();
            throw;
        }
        return new CdpConnection(ws);
    }

    /// <summary>
    /// Sends a CDP command without typed params (e.g. Page.enable, Page.getFrameTree)
    /// and deserializes the result element using the supplied source-generated metadata.
    /// </summary>
    public Task<TResult> SendAsync<TResult>(
        string method,
        JsonTypeInfo<TResult> resultInfo,
        string? sessionId,
        CancellationToken cancellationToken)
        where TResult : class
        => SendCoreAsync<object, TResult>(method, paramsValue: null, paramsInfo: null, resultInfo, sessionId, cancellationToken);

    /// <summary>
    /// Sends a CDP command with typed params and deserializes the result.
    /// </summary>
    public Task<TResult> SendAsync<TParams, TResult>(
        string method,
        TParams paramsValue,
        JsonTypeInfo<TParams> paramsInfo,
        JsonTypeInfo<TResult> resultInfo,
        string? sessionId,
        CancellationToken cancellationToken)
        where TParams : class
        where TResult : class
        => SendCoreAsync(method, paramsValue, paramsInfo, resultInfo, sessionId, cancellationToken);

    /// <summary>
    /// Sends a CDP command whose result is not consumed.
    /// </summary>
    public async Task SendVoidAsync<TParams>(
        string method,
        TParams paramsValue,
        JsonTypeInfo<TParams> paramsInfo,
        string? sessionId,
        CancellationToken cancellationToken)
        where TParams : class
    {
        _ = await SendCoreRawAsync(method, paramsValue, paramsInfo, sessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendVoidAsync(string method, string? sessionId, CancellationToken cancellationToken)
    {
        _ = await SendCoreRawAsync<object>(method, paramsValue: null, paramsInfo: null, sessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers a one-shot subscription for a CDP event. The returned
    /// <see cref="CdpEventSubscription"/> resolves on the first matching
    /// (method, sessionId) message; pass <c>null</c> for <paramref name="sessionId"/>
    /// to match any session. The subscription must be registered <em>before</em>
    /// the action that triggers the event to avoid races.
    /// </summary>
    public CdpEventSubscription SubscribeOnce(string method, string? sessionId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var token = Interlocked.Increment(ref _nextSubscriptionToken);
        var subscription = new CdpEventSubscription(
            method,
            sessionId,
            _ => _subscriptions.TryRemove(token, out CdpEventSubscription? _));
        _subscriptions[token] = subscription;
        return subscription;
    }

    private async Task<TResult> SendCoreAsync<TParams, TResult>(
        string method,
        TParams? paramsValue,
        JsonTypeInfo<TParams>? paramsInfo,
        JsonTypeInfo<TResult> resultInfo,
        string? sessionId,
        CancellationToken cancellationToken)
        where TParams : class
        where TResult : class
    {
        var element = await SendCoreRawAsync(method, paramsValue, paramsInfo, sessionId, cancellationToken).ConfigureAwait(false);
        var result = element.Deserialize(resultInfo);
        return result ?? throw new CdpException($"CDP method '{method}' returned a null result.");
    }

    private async Task<JsonElement> SendCoreRawAsync<TParams>(
        string method,
        TParams? paramsValue,
        JsonTypeInfo<TParams>? paramsInfo,
        string? sessionId,
        CancellationToken cancellationToken)
        where TParams : class
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            using var buffer = new MemoryStream();
            var writer = new Utf8JsonWriter(buffer);
            await using (writer.ConfigureAwait(false))
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", id);
                writer.WriteString("method", method);
                if (sessionId is not null)
                {
                    writer.WriteString("sessionId", sessionId);
                }
                if (paramsValue is not null && paramsInfo is not null)
                {
                    writer.WritePropertyName("params");
                    JsonSerializer.Serialize(writer, paramsValue, paramsInfo);
                }
                writer.WriteEndObject();
            }

            var payload = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
            await _ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);

            return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer.AsMemory(), _cts.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        FailAllPending(new CdpException("WebSocket closed by remote."));
                        return;
                    }
                    await ms.WriteAsync(buffer.AsMemory(0, result.Count), _cts.Token).ConfigureAwait(false);
                } while (!result.EndOfMessage);

                ms.Position = 0;
                Dispatch(ms);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown path
        }
        catch (WebSocketException ex)
        {
            FailAllPending(ex);
        }
        catch (JsonException ex)
        {
            FailAllPending(ex);
        }
        catch (IOException ex)
        {
            FailAllPending(ex);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Dispatch(Stream stream)
    {
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number)
        {
            DispatchResponse(root, idElement.GetInt32());
            return;
        }

        DispatchEvent(root);
    }

    private void DispatchResponse(JsonElement root, int id)
    {
        if (!_pending.TryRemove(id, out var tcs))
        {
            return;
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            var err = errorElement.Deserialize(CdpJsonContext.Default.CdpErrorPayload);
            tcs.TrySetException(new CdpException(err?.Code ?? -1, err?.Message ?? "CDP error"));
            return;
        }

        if (root.TryGetProperty("result", out var resultElement))
        {
            tcs.TrySetResult(resultElement.Clone());
        }
        else
        {
            tcs.TrySetResult(default);
        }
    }

    private void DispatchEvent(JsonElement root)
    {
        if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var method = methodElement.GetString();
        if (method is null)
        {
            return;
        }

        string? sessionId = null;
        if (root.TryGetProperty("sessionId", out var sessionElement) && sessionElement.ValueKind == JsonValueKind.String)
        {
            sessionId = sessionElement.GetString();
        }

        JsonElement paramsClone = default;
        var hasParams = root.TryGetProperty("params", out var paramsElement);
        if (hasParams)
        {
            paramsClone = paramsElement.Clone();
        }

        foreach (var kvp in _subscriptions)
        {
            var sub = kvp.Value;
            if (!string.Equals(sub.Method, method, StringComparison.Ordinal))
            {
                continue;
            }
            if (sub.SessionId is not null && !string.Equals(sub.SessionId, sessionId, StringComparison.Ordinal))
            {
                continue;
            }

            if (_subscriptions.TryRemove(kvp.Key, out _))
            {
                sub.TryComplete(paramsClone);
            }
        }
    }

    private void FailAllPending(Exception exception)
    {
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetException(exception);
        }
        _pending.Clear();

        foreach (var kvp in _subscriptions)
        {
            kvp.Value.TryFail(exception);
        }
        _subscriptions.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }

        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            await _receiveLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }

        _ws.Dispose();
        _cts.Dispose();
    }
}
