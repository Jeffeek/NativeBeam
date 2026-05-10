namespace NativeBeam.Pdf;

public enum PdfPaperFormat
{
    A4,
    A3,
    Letter,
    Legal,
    Tabloid,
}

public enum PdfOrientation
{
    Portrait,
    Landscape,
}

/// <summary>
/// Strongly-typed, AOT-friendly PDF rendering options. Margin units are inches,
/// matching the Chromium DevTools Protocol Page.printToPDF surface directly.
/// </summary>
public readonly record struct PdfOptions(
    PdfPaperFormat Format = PdfPaperFormat.A4,
    PdfOrientation Orientation = PdfOrientation.Portrait,
    bool PrintBackground = true,
    double Scale = 1.0,
    double MarginTop = 0.0,
    double MarginRight = 0.0,
    double MarginBottom = 0.0,
    double MarginLeft = 0.0,
    int NavigationTimeoutMs = 30_000,
    int LoadEventTimeoutMs = 30_000,
    string? PreRenderScript = null)
{
    // NOTE: For a record struct, `new()` and `default(T)` both zero-init —
    // they do NOT apply the primary-ctor defaults. We must explicitly invoke
    // the primary constructor so Scale=1.0 et al. take effect; otherwise
    // Chrome rejects PDF rendering with "scale is outside of [0.1 - 2] range".
    public static PdfOptions Default { get; } = new PdfOptions(Format: PdfPaperFormat.A4);

    internal (double Width, double Height) GetPaperSizeInches() => Format switch
    {
        PdfPaperFormat.A4 => (8.27, 11.69),
        PdfPaperFormat.A3 => (11.69, 16.54),
        PdfPaperFormat.Letter => (8.5, 11.0),
        PdfPaperFormat.Legal => (8.5, 14.0),
        PdfPaperFormat.Tabloid => (11.0, 17.0),
        _ => (8.27, 11.69),
    };
}
