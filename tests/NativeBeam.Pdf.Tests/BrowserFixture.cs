using NativeBeam.Pdf.Cdp;

namespace NativeBeam.Pdf.Tests;

/// <summary>
/// Shared xUnit fixture that boots a single Chromium-backed renderer for the
/// whole test class. Honours <c>CHROME_PATH</c> when set (used by CI lanes
/// where Chrome lives outside the standard install paths) and surfaces a
/// human-readable reason via <see cref="UnavailableReason"/> so individual
/// tests can <c>Skip.IfNot(...)</c> instead of failing the run wholesale.
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private const string ProbeHtml = "<!doctype html><html><body>probe</body></html>";

    public AotPdfRenderer? Renderer { get; private set; }

    public bool IsAvailable { get; private set; }

    public string? UnavailableReason { get; private set; }

    public bool IsRunningInGitHubActions { get; }
        = string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public string? ChromePathFromEnvironment { get; }
        = NormalizePath(Environment.GetEnvironmentVariable("CHROME_PATH"));

    public async Task InitializeAsync()
    {
        var options = new ChromeLaunchOptions(ExecutablePath: ChromePathFromEnvironment);
        var renderer = new AotPdfRenderer(options);
        try
        {
            // Force the lazy launch + WebSocket connect now so a missing browser
            // surfaces here rather than from inside an individual test.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _ = await renderer.RenderHtmlAsync(ProbeHtml, PdfOptions.Default, cts.Token);
            Renderer = renderer;
            IsAvailable = true;
        }
        catch (Exception ex) when (ex is CdpException or System.ComponentModel.Win32Exception or TimeoutException or OperationCanceledException)
        {
            await renderer.DisposeAsync();
            UnavailableReason = BuildUnavailableMessage(ex);
            IsAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (Renderer is not null)
        {
            await Renderer.DisposeAsync();
            Renderer = null;
        }
    }

    private string BuildUnavailableMessage(Exception ex)
    {
        var ci = IsRunningInGitHubActions ? " (GitHub Actions)" : string.Empty;
        var hint = ChromePathFromEnvironment is null
            ? "Set the CHROME_PATH environment variable to a Chromium-based browser executable."
            : $"CHROME_PATH was '{ChromePathFromEnvironment}' but the browser failed to launch.";
        return $"No usable Chromium browser{ci}: {ex.Message}. {hint}";
    }

    private static string? NormalizePath(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
