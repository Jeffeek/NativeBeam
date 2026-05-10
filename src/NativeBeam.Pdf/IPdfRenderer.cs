namespace NativeBeam.Pdf;

/// <summary>
/// Contract for HTML-to-PDF rendering. Implementations must be safe to share
/// across concurrent calls and must dispose any underlying browser resources.
/// </summary>
public interface IPdfRenderer : IAsyncDisposable
{
    /// <summary>
    /// Renders an HTML document to a PDF byte buffer.
    /// </summary>
    /// <param name="html">Full HTML document source.</param>
    /// <param name="options">PDF rendering options.</param>
    /// <param name="cancellationToken">Cancels both navigation and PDF generation.</param>
    Task<byte[]> RenderHtmlAsync(
        string html,
        PdfOptions options,
        CancellationToken cancellationToken = default);
}
