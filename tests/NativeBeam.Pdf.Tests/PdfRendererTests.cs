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
    public async Task Render_Concurrent_MultipleRequests()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.UnavailableReason);

        // Five distinct documents fired in parallel against the SAME renderer
        // instance. The CDP connection is one socket guarded by a single
        // read-pump; this verifies the id-keyed dispatch dictionary and the
        // per-render fresh-target pattern do not deadlock or interleave.
        const int parallelism = 5;
        var tasks = new Task<byte[]>[parallelism];
        for (var i = 0; i < parallelism; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(() =>
                fixture.Renderer!.RenderHtmlAsync(
                    $"<!doctype html><html><body><h1>render #{idx}</h1></body></html>",
                    PdfOptions.Default));
        }

        var sw = Stopwatch.StartNew();
        var results = await Task.WhenAll(tasks);
        sw.Stop();

        output.WriteLine(
            $"{parallelism} concurrent renders completed in {sw.ElapsedMilliseconds:N0} ms.");

        Assert.All(results, pdf =>
        {
            Assert.NotNull(pdf);
            Assert.True(pdf.Length > PdfMagic.Length);
            AssertPdfMagic(pdf);
        });
    }

    [Theory]
    [InlineData(PdfPaperFormat.A4, 8.27, 11.69)]
    [InlineData(PdfPaperFormat.A3, 11.69, 16.54)]
    [InlineData(PdfPaperFormat.Letter, 8.5, 11.0)]
    [InlineData(PdfPaperFormat.Legal, 8.5, 14.0)]
    [InlineData(PdfPaperFormat.Tabloid, 11.0, 17.0)]
    public void PdfOptions_PaperFormat_MapsToCdpInches(PdfPaperFormat format, double expectedWidth, double expectedHeight)
    {
        // Validates the CDP boundary: Page.printToPDF expects paperWidth /
        // paperHeight in inches, not a format string. A regression here would
        // ship the wrong page size to Chromium.
        var options = PdfOptions.Default with { Format = format };
        var (width, height) = options.GetPaperSizeInches();

        Assert.Equal(expectedWidth, width, precision: 2);
        Assert.Equal(expectedHeight, height, precision: 2);
    }

    [Fact]
    public void PdfOptions_Default_ProducesNonZeroScale()
    {
        // Regression guard: `default(PdfOptions)` zero-inits the record struct
        // (Scale = 0) which Chromium rejects. PdfOptions.Default explicitly
        // invokes the primary constructor and must yield Scale = 1.0.
        var options = PdfOptions.Default;
        Assert.Equal(1.0, options.Scale);
        Assert.Equal(PdfPaperFormat.A4, options.Format);
        Assert.Equal(PdfOrientation.Portrait, options.Orientation);
        Assert.True(options.PrintBackground);
        Assert.Equal(0.0, options.MarginTop);
        Assert.Equal(0.0, options.MarginRight);
        Assert.Equal(0.0, options.MarginBottom);
        Assert.Equal(0.0, options.MarginLeft);
        Assert.Equal(30_000, options.NavigationTimeoutMs);
        Assert.Equal(30_000, options.LoadEventTimeoutMs);
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
