using System.Text.Json;
using NativeBeam.Pdf.Cdp;

namespace NativeBeam.Pdf;

/// <summary>
/// Native AOT-compatible HTML-to-PDF renderer driven entirely through the
/// Chromium DevTools Protocol over <see cref="System.Net.WebSockets.ClientWebSocket"/>.
/// All payloads are (de)serialized via the source-generated
/// <see cref="CdpJsonContext"/> — no reflection, no dynamic code emission.
/// </summary>
public sealed class AotPdfRenderer : IPdfRenderer
{
    private readonly ChromeLaunchOptions _launchOptions;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private ChromeLauncher? _launcher;
    private CdpConnection? _connection;
    private int _disposed;

    public AotPdfRenderer() : this(new ChromeLaunchOptions())
    {
    }

    public AotPdfRenderer(ChromeLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(launchOptions);
        _launchOptions = launchOptions;
    }

    public async Task<byte[]> RenderHtmlAsync(
        string html,
        PdfOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var conn = await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

        // 1. Create a new target (about:blank) at the browser level.
        var created = await conn.SendAsync(
            "Target.createTarget",
            new TargetCreateTargetParams("about:blank"),
            CdpJsonContext.Default.TargetCreateTargetParams,
            CdpJsonContext.Default.TargetCreateTargetResult,
            sessionId: null,
            cancellationToken).ConfigureAwait(false);

        try
        {
            // 2. Attach (flatten:true => session-scoped routing on the same socket).
            var attached = await conn.SendAsync(
                "Target.attachToTarget",
                new TargetAttachToTargetParams(created.TargetId, Flatten: true),
                CdpJsonContext.Default.TargetAttachToTargetParams,
                CdpJsonContext.Default.TargetAttachToTargetResult,
                sessionId: null,
                cancellationToken).ConfigureAwait(false);

            var sessionId = attached.SessionId;

            // 3. Enable the Page domain on the session.
            await conn.SendVoidAsync("Page.enable", sessionId, cancellationToken).ConfigureAwait(false);

            // 4. Resolve the main frame id.
            var frameTree = await conn.SendAsync(
                "Page.getFrameTree",
                CdpJsonContext.Default.PageGetFrameTreeResult,
                sessionId,
                cancellationToken).ConfigureAwait(false);

            // 5. Subscribe to the load event BEFORE triggering navigation so the
            //    waiter is registered even for very fast loads.
            using var loadFired = conn.SubscribeOnce("Page.loadEventFired", sessionId);

            // 6. Inject the HTML payload directly into the main frame.
            await conn.SendVoidAsync(
                "Page.setDocumentContent",
                new PageSetDocumentContentParams(frameTree.FrameTree.Frame.Id, html),
                CdpJsonContext.Default.PageSetDocumentContentParams,
                sessionId,
                cancellationToken).ConfigureAwait(false);

            // 7. Wait for the page (including external resources) to finish loading.
            try
            {
                var loadPayload = await loadFired.Task
                    .WaitAsync(TimeSpan.FromMilliseconds(options.LoadEventTimeoutMs), cancellationToken)
                    .ConfigureAwait(false);
                _ = loadPayload.Deserialize(CdpJsonContext.Default.PageLoadEventFiredEvent);
            }
            catch (TimeoutException ex)
            {
                throw new CdpException(
                    $"Page.loadEventFired did not fire within {options.LoadEventTimeoutMs} ms.", ex);
            }

            // 8. Print to PDF.
            var (paperWidth, paperHeight) = options.GetPaperSizeInches();
            var pdf = await conn.SendAsync(
                "Page.printToPDF",
                new PagePrintToPdfParams(
                    PrintBackground: options.PrintBackground,
                    Landscape: options.Orientation == PdfOrientation.Landscape,
                    Scale: options.Scale,
                    PaperWidth: paperWidth,
                    PaperHeight: paperHeight,
                    MarginTop: options.MarginTop,
                    MarginBottom: options.MarginBottom,
                    MarginLeft: options.MarginLeft,
                    MarginRight: options.MarginRight),
                CdpJsonContext.Default.PagePrintToPdfParams,
                CdpJsonContext.Default.PagePrintToPdfResult,
                sessionId,
                cancellationToken).ConfigureAwait(false);

            return Convert.FromBase64String(pdf.Data);
        }
        finally
        {
            try
            {
                await conn.SendVoidAsync(
                    "Target.closeTarget",
                    new TargetCloseTargetParams(created.TargetId),
                    CdpJsonContext.Default.TargetCloseTargetParams,
                    sessionId: null,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (CdpException)
            {
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability", "CA2000",
        Justification = "ChromeLauncher ownership transfers to the field on success and is disposed on failure.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability", "CA1508",
        Justification = "Double-checked locking; the inner null check is reachable after the semaphore is acquired.")]
    private async Task<CdpConnection> EnsureConnectionAsync(CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref _connection);
        if (existing is not null)
        {
            return existing;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = Volatile.Read(ref _connection);
            if (existing is not null)
            {
                return existing;
            }

            ChromeLauncher? launcher = null;
            try
            {
                launcher = await ChromeLauncher.LaunchAsync(_launchOptions, cancellationToken).ConfigureAwait(false);
                var connection = await CdpConnection.ConnectAsync(launcher.DebuggerWebSocketUri, cancellationToken).ConfigureAwait(false);
                _launcher = launcher;
                _connection = connection;
                launcher = null;
                return connection;
            }
            finally
            {
                if (launcher is not null)
                {
                    await launcher.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        if (_launcher is not null)
        {
            await _launcher.DisposeAsync().ConfigureAwait(false);
        }
        _initLock.Dispose();
    }
}
