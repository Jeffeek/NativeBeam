# NativeBeam

High-performance, **Native AOT-compatible** HTML-to-PDF rendering for .NET, driven by a direct, reflection-free implementation of the Chromium DevTools Protocol.

[![Build](https://github.com/Jeffeek/NativeBeam/actions/workflows/ci.yml/badge.svg)](https://github.com/Jeffeek/NativeBeam/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/NativeBeam.Pdf.svg)](https://www.nuget.org/packages/NativeBeam.Pdf)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## Why NativeBeam?

NativeBeam exists because every existing .NET browser-automation stack assumes a JIT runtime.

| | NativeBeam | Microsoft.Playwright | PuppeteerSharp |
|---|---|---|---|
| Native AOT | ✅ Library is `IsAotCompatible`, IL2026/IL3050 promoted to errors | ❌ Reflection-heavy driver IPC | ❌ Reflection over CDP DTOs |
| Reflection at runtime | ❌ None — all JSON via `JsonSerializerContext` source generators | ✅ Yes | ✅ Yes |
| Driver dependency | ❌ None — direct WebSocket to Chromium | ✅ Bundled Node-based driver | ❌ None |
| Trim warnings as errors | ✅ Solution-wide | ❌ | ❌ |
| Single-file publish | ✅ | Partial | Partial |
| Cold start | Process launch + WS connect | Driver bootstrap + browser | Browser launch |

The library targets `net8.0` and `net10.0`, ships zero unmanaged dependencies, and does not pull in a JavaScript runtime.

## The Pivot

The first iteration wrapped `Microsoft.Playwright`. It worked, but Playwright performs reflection over its driver IPC layer — `IL2026`/`IL3050` warnings leak through any AOT publish, and the bundled Node driver is incompatible with single-file native binaries. There is no flag that fixes this.

So we pivoted: NativeBeam talks to Chromium directly over the **DevTools Protocol** (CDP). A minimal `ClientWebSocket` pump dispatches responses by `id` and events by `(method, sessionId)`; every payload is (de)serialized through a source-generated `JsonSerializerContext`. There is no `JsonSerializer.Serialize<T>(...)` reflection path, no `Activator.CreateInstance`, no DI container. The full surface is `Process.Start` → `ClientWebSocket.ConnectAsync` → typed `Send`/`Receive` over CDP — nothing else.

## Installation

```bash
dotnet add package NativeBeam.Pdf
```

## Quick Start

```csharp
using NativeBeam.Pdf;

const string html = """
    <!doctype html>
    <html>
      <body>
        <h1>NativeBeam</h1>
        <p>AOT-compatible HTML to PDF.</p>
      </body>
    </html>
    """;

await using var renderer = new AotPdfRenderer();

byte[] pdf = await renderer.RenderHtmlAsync(html, PdfOptions.Default);

await File.WriteAllBytesAsync("out.pdf", pdf);
```

`PdfOptions` is a `readonly record struct` covering paper format, orientation, scale, margins (in inches), navigation timeout, and load-event timeout. The renderer is safe to reuse across renders; one Chromium process is launched on first call and reused until disposal.

> **Important:** use `PdfOptions.Default`, not `default(PdfOptions)`. Record-struct primary-constructor defaults are not applied by `default(T)` or `new()` — the static `Default` invokes the primary constructor explicitly and is the only correct default.

### Publishing as Native AOT

```bash
dotnet publish -c Release -r linux-x64 /p:PublishAot=true
```

The CI publish workflow runs this against the demo project on every release tag and fails the publish if AOT regresses.

## Infrastructure Requirements

A Chromium-based browser must be available on the host. NativeBeam auto-detects (in this order, by platform):

- **Windows:** Google Chrome, Brave (`%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe`), Microsoft Edge.
- **macOS:** Google Chrome, Chromium, Brave Browser, Microsoft Edge.
- **Linux:** `google-chrome`, `chromium`, `chromium-browser`, `brave-browser`, `microsoft-edge` (under `/usr/bin`, `/snap/bin`).

Override the search by passing `ChromeLaunchOptions(ExecutablePath: "/path/to/chrome")` to the `AotPdfRenderer` constructor. CI runs that publish a `CHROME_PATH` environment variable will be honored by the test fixture.

## Docker

A `.NET 10` image plus the headless-Chrome runtime libraries:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Chromium + headless runtime deps. `chromium` here is the OS package, not the
# Snap. Add fonts as needed for your content.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        chromium \
        ca-certificates \
        fonts-liberation \
        fonts-noto-color-emoji \
 && rm -rf /var/lib/apt/lists/*

ENV CHROME_PATH=/usr/bin/chromium

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "YourApp.dll"]
```

For an AOT-published binary, swap the runtime base for `mcr.microsoft.com/dotnet/runtime-deps:10.0` and copy the native binary in place of the managed publish output. `CHROME_PATH` is read by `ChromeLauncher` as a fallback when the standard install paths aren't present, which is the common case in distroless / minimal images.

## Roadmap

- **PDF/A** — emit ISO 19005-conformant output for archival use cases (currently emits standard PDF 1.4).
- **Header / footer templates** — surface CDP's `headerTemplate`/`footerTemplate` parameters through `PdfOptions`, plus first-class support for page-number / total-pages tokens.
- **JavaScript injection** — `Page.addScriptToEvaluateOnNewDocument` and a typed `EvaluateAsync<T>` for parameterised templates.
- **Multi-document pipeline** — render N HTML inputs through a single Chromium target pool with bounded concurrency.
- **Event surface** — `Network.responseReceived`, `Page.frameNavigated`, etc., exposed alongside the existing one-shot `SubscribeOnce` API.

## License

[MIT](LICENSE)
