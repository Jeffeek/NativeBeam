using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace NativeBeam.Pdf.Tests;

public sealed class PdfRendererTests(BrowserFixture fixture, ITestOutputHelper output) : IClassFixture<BrowserFixture>
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

    [SkippableFact]
    public async Task EvaluateScriptAsync_ReturnsJsonValue()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);

        var sum = await fixture.Renderer!.EvaluateScriptAsync("40 + 2");
        Assert.Equal(JsonValueKind.Number, sum.ValueKind);
        Assert.Equal(42, sum.GetInt32());

        var awaited = await fixture.Renderer.EvaluateScriptAsync(
            "Promise.resolve({ ok: true, name: 'beam' })");
        Assert.Equal(JsonValueKind.Object, awaited.ValueKind);
        Assert.True(awaited.GetProperty("ok").GetBoolean());
        Assert.Equal("beam", awaited.GetProperty("name").GetString());
    }

    [SkippableFact]
    public async Task RenderHtml_PreRenderScript_MutatesDom()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);

        const string html = """
            <!doctype html>
            <html>
              <body>
                <h1 id="t">PLACEHOLDER</h1>
              </body>
            </html>
            """;

        var options = PdfOptions.Default with
        {
            PreRenderScript = "document.getElementById('t').textContent = 'rewritten';",
        };

        var pdf = await fixture.Renderer!.RenderHtmlAsync(html, options);

        Assert.NotNull(pdf);
        AssertPdfMagic(pdf);
    }

    [SkippableFact]
    public async Task Render_LargeHtml_PerformanceCheck()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);

        // Generous upper bound to keep the test reliable on shared CI runners.
        // The intent is to catch a clear regression (10x slower than the
        // current ~1-3s baseline), not to gate on absolute timing.
        const int budgetMs = 60_000;
        const int targetPages = 50;

        var html = BuildLargeHtml(targetPages);

        var sw = Stopwatch.StartNew();
        var pdf = await fixture.Renderer!.RenderHtmlAsync(html, PdfOptions.Default);
        sw.Stop();

        var bytesPerPage = pdf.Length / Math.Max(1, targetPages);
        output.WriteLine(
            $"Rendered ~{targetPages} pages ({pdf.Length:N0} bytes, ~{bytesPerPage:N0} B/page) in {sw.ElapsedMilliseconds:N0} ms.");

        Assert.NotNull(pdf);
        AssertPdfMagic(pdf);
        Assert.True(
            sw.ElapsedMilliseconds < budgetMs,
            $"Render exceeded the {budgetMs} ms budget (took {sw.ElapsedMilliseconds} ms). Investigate before merging.");
    }

    private static string BuildLargeHtml(int pageCount)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.Append("""
            <!doctype html>
            <html>
              <head>
                <meta charset="utf-8">
                <title>perf</title>
                <style>
                  body { font-family: sans-serif; margin: 0; }
                  .page { page-break-after: always; padding: 32px; }
                  h2 { border-bottom: 1px solid #aaa; padding-bottom: 4px; }
                  table { width: 100%; border-collapse: collapse; }
                  td, th { padding: 4px 8px; border: 1px solid #ddd; font-size: 11px; }
                </style>
              </head>
              <body>
            """);

        for (var i = 1; i <= pageCount; i++)
        {
            sb.Append("<section class=\"page\">");
            sb.Append("<h2>Page ").Append(i).Append("</h2>");
            sb.Append("<p>Synthetic content used to establish a rendering baseline.</p>");
            sb.Append("<table><thead><tr><th>#</th><th>Lorem</th><th>Ipsum</th><th>Sum</th></tr></thead><tbody>");
            for (var row = 0; row < 30; row++)
            {
                sb.Append("<tr><td>").Append(row + 1)
                  .Append("</td><td>Lorem ipsum dolor sit amet</td>")
                  .Append("<td>consectetur adipiscing elit</td>")
                  .Append("<td>").Append(row * (i + 1)).Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
            sb.Append("</section>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AssertPdfMagic(byte[] pdf)
    {
        var prefix = pdf.AsSpan(0, PdfMagic.Length);
        Assert.True(
            prefix.SequenceEqual(PdfMagic),
            $"Expected PDF magic '%PDF-' but got '{Encoding.ASCII.GetString(prefix)}'.");
    }
}
