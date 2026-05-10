namespace NativeBeam.Pdf;

/// <summary>
/// Contract for HTML-to-PDF rendering implementations.
/// </summary>
/// <remarks>
/// Implementations are expected to be safe to share across concurrent
/// <see cref="RenderHtmlAsync(string, PdfOptions, CancellationToken)"/> calls and
/// must release any underlying browser resources via
/// <see cref="IAsyncDisposable.DisposeAsync"/>. The shipped
/// <see cref="AotPdfRenderer"/> launches a single Chromium process on first
/// render and reuses it for the lifetime of the instance.
/// </remarks>
public interface IPdfRenderer : IAsyncDisposable
{
    /// <summary>
    /// Renders a complete HTML document to a PDF byte buffer.
    /// </summary>
    /// <param name="html">
    /// A complete HTML document (including <c>&lt;!doctype&gt;</c>,
    /// <c>&lt;html&gt;</c>, <c>&lt;head&gt;</c>, and <c>&lt;body&gt;</c>).
    /// External resources (CSS, images, fonts) are loaded by the headless
    /// browser before the PDF is produced; absolute or <c>data:</c> URLs are
    /// supported.
    /// </param>
    /// <param name="options">
    /// Page geometry, margins, scale, and timeouts. Use
    /// <see cref="PdfOptions.Default"/> rather than <c>default(PdfOptions)</c>
    /// — record-struct primary-constructor defaults are not applied by the
    /// implicit zero-init constructor and would ship <c>Scale = 0</c>, which
    /// Chromium rejects.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels both the navigation phase and the
    /// <c>Page.printToPDF</c> command. Cancellation after the underlying
    /// browser target has been created may still incur a best-effort
    /// <c>Target.closeTarget</c> cleanup.
    /// </param>
    /// <returns>
    /// A task whose result is the rendered PDF as a fresh byte array. The
    /// first five bytes are always the PDF magic <c>%PDF-</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="html"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The renderer has already been disposed.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was signalled.
    /// </exception>
    /// <exception cref="Cdp.CdpException">
    /// The headless browser failed to launch, the WebSocket session was lost,
    /// or the page did not raise <c>Page.loadEventFired</c> within
    /// <see cref="PdfOptions.LoadEventTimeoutMs"/>.
    /// </exception>
    Task<byte[]> RenderHtmlAsync(
        string html,
        PdfOptions options,
        CancellationToken cancellationToken = default);
}
