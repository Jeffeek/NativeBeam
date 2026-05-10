using System.Text.Json;

namespace NativeBeam.Pdf.Cdp;

/// <summary>
/// One-shot CDP event subscription. Resolves its <see cref="Task"/> with the
/// raw <c>params</c> element of the first matching event, then unregisters
/// itself. Disposing before the event fires cancels the subscription.
/// </summary>
internal sealed class CdpEventSubscription : IDisposable
{
    private readonly TaskCompletionSource<JsonElement> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<CdpEventSubscription>? _unregister;

    internal CdpEventSubscription(string method, string? sessionId, Action<CdpEventSubscription> unregister)
    {
        Method = method;
        SessionId = sessionId;
        _unregister = unregister;
    }

    public string Method { get; }

    public string? SessionId { get; }

    public Task<JsonElement> Task => _tcs.Task;

    internal bool TryComplete(JsonElement payload)
    {
        if (!_tcs.TrySetResult(payload))
        {
            return false;
        }
        Interlocked.Exchange(ref _unregister, null);
        return true;
    }

    internal void TryFail(Exception exception) => _tcs.TrySetException(exception);

    public void Dispose()
    {
        var unregister = Interlocked.Exchange(ref _unregister, null);
        unregister?.Invoke(this);
        _tcs.TrySetCanceled();
    }
}
