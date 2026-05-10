using NativeBeam.Pdf;

const string html = """
    <!doctype html>
    <html>
      <head><meta charset="utf-8"><title>NativeBeam</title></head>
      <body>
        <h1>NativeBeam</h1>
        <p>Native AOT-compatible HTML-to-PDF rendering.</p>
      </body>
    </html>
    """;

await using var renderer = new AotPdfRenderer();

var pdf = await renderer.RenderHtmlAsync(html, PdfOptions.Default);

var output = Path.Combine(AppContext.BaseDirectory, "out.pdf");
await File.WriteAllBytesAsync(output, pdf);

Console.WriteLine($"Wrote {pdf.Length:N0} bytes to {output}");
