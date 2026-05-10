using NativeBeam.Pdf;
using Scriban;
using Scriban.Runtime;

// ---------------------------------------------------------------------------
// Goal: render an HTML invoice from a Scriban template, then hand the result
// to NativeBeam for PDF generation. Keep the data path AOT-safe.
//
// AOT note: Scriban's `Template.Render(object model)` overload reflects over
// the model's properties, which trips IL2026/IL3050 under PublishAot. The
// dictionary-style `ScriptObject` overload is reflection-free at the binding
// boundary - that's the only Scriban API we use here. Treat any Scriban
// extension that takes a CLR model as AOT-unsafe.
// ---------------------------------------------------------------------------

const string templateSource = """
    <!doctype html>
    <html>
      <head>
        <meta charset="utf-8">
        <title>Invoice {{ invoice.number }}</title>
        <style>
          body { font-family: sans-serif; margin: 32px; color: #222; }
          h1   { border-bottom: 2px solid #333; padding-bottom: 8px; }
          table { width: 100%; border-collapse: collapse; margin-top: 16px; }
          th, td { padding: 8px; border-bottom: 1px solid #ddd; text-align: left; }
          tfoot td { font-weight: 600; }
        </style>
      </head>
      <body>
        <h1>Invoice {{ invoice.number }}</h1>
        <p>Issued: {{ invoice.issued }}</p>
        <p>Customer: {{ invoice.customer }}</p>
        <table>
          <thead><tr><th>Item</th><th>Qty</th><th>Unit</th><th>Total</th></tr></thead>
          <tbody>
            {{- for line in invoice.lines }}
            <tr>
              <td>{{ line.description }}</td>
              <td>{{ line.quantity }}</td>
              <td>{{ line.unit }}</td>
              <td>{{ line.total }}</td>
            </tr>
            {{- end }}
          </tbody>
          <tfoot>
            <tr><td colspan="3">Total</td><td>{{ invoice.total }}</td></tr>
          </tfoot>
        </table>
      </body>
    </html>
    """;

var template = Template.Parse(templateSource);
if (template.HasErrors)
{
    Console.Error.WriteLine("Template parse failed:");
    foreach (var msg in template.Messages)
    {
        Console.Error.WriteLine($"  {msg}");
    }
    return 1;
}

// Build the model as a ScriptObject. This is the AOT-safe binding path:
// Scriban does not reflect on CLR types at render time, only walks the
// dictionary we hand it.
var lines = new ScriptArray
{
    NewLine("Engineering retainer", quantity: 40, unit: "150.00", total: "6,000.00"),
    NewLine("On-call coverage",     quantity: 1,  unit: "1,200.00", total: "1,200.00"),
    NewLine("Toolchain license",    quantity: 3,  unit: "99.00",    total: "297.00"),
};

var invoice = new ScriptObject
{
    ["number"]   = "INV-2026-0042",
    ["issued"]   = DateTime.UtcNow.ToString("yyyy-MM-dd"),
    ["customer"] = "ACME Robotics, Inc.",
    ["lines"]    = lines,
    ["total"]    = "7,497.00",
};

var context = new TemplateContext { StrictVariables = true };
var globals = new ScriptObject { ["invoice"] = invoice };
context.PushGlobal(globals);

var html = await template.RenderAsync(context);

await using var renderer = new AotPdfRenderer();
var pdf = await renderer.RenderHtmlAsync(html, PdfOptions.Default);

var output = Path.Combine(AppContext.BaseDirectory, "invoice.pdf");
await File.WriteAllBytesAsync(output, pdf);

Console.Out.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
    $"Wrote {pdf.Length:N0} bytes to {output}"));
return 0;

static ScriptObject NewLine(string description, int quantity, string unit, string total) => new()
{
    ["description"] = description,
    ["quantity"]    = quantity,
    ["unit"]        = unit,
    ["total"]       = total,
};
