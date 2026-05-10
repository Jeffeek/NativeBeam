using System.Text;

namespace NativeBeam.Pdf.Tests;

public sealed class PdfRendererTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    // 1x1 transparent PNG.
    private const string PngDataUrl =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    // body{background:#f0f0f0;font-family:sans-serif;margin:24px}h1{color:#333}
    private const string CssDataUrl =
        "data:text/css;base64,Ym9keXtiYWNrZ3JvdW5kOiNmMGYwZjA7Zm9udC1mYW1pbHk6c2Fucy1zZXJpZjttYXJnaW46MjRweH1oMXtjb2xvcjojMzMzfQ==";

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    [SkippableFact]
    public async Task Render_SimpleHtml_ReturnsPdfBytes()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);

        const string html = """
            <!doctype html>
            <html>
              <head><meta charset="utf-8"><title>simple</title></head>
              <body><h1>Hello, NativeBeam</h1><p>Single-document smoke test.</p></body>
            </html>
            """;

        var pdf = await fixture.Renderer!.RenderHtmlAsync(html, PdfOptions.Default);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > PdfMagic.Length, $"PDF was unexpectedly small: {pdf.Length} bytes.");
        AssertPdfMagic(pdf);
    }

    [SkippableFact]
    public async Task Render_WithExternalResources_Success()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);

        var html = $$"""
            <!doctype html>
            <html>
              <head>
                <meta charset="utf-8">
                <title>external resources</title>
                <link rel="stylesheet" href="{{CssDataUrl}}">
              </head>
              <body>
                <h1>External resource load test</h1>
                <p>Validates that <code>Page.loadEventFired</code> is awaited before rendering.</p>
                <img src="{{PngDataUrl}}" alt="dot" width="64" height="64" />
              </body>
            </html>
            """;

        var pdf = await fixture.Renderer!.RenderHtmlAsync(html, PdfOptions.Default);

        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 256, $"PDF was unexpectedly small: {pdf.Length} bytes.");
        AssertPdfMagic(pdf);

        // Integration test: persist the artifact under a temp folder so failing
        // CI runs can attach it as a build artifact for inspection.
        var dir = Path.Combine(Path.GetTempPath(), "nativebeam-tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"external-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, pdf);

        Assert.True(File.Exists(path), $"Expected PDF artifact at {path}.");
    }

    private static void AssertPdfMagic(byte[] pdf)
    {
        var prefix = pdf.AsSpan(0, PdfMagic.Length);
        Assert.True(
            prefix.SequenceEqual(PdfMagic),
            $"Expected PDF magic '%PDF-' but got '{Encoding.ASCII.GetString(prefix)}'.");
    }
}
