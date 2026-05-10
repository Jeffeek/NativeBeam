using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NativeBeam.Pdf.Cdp;

public sealed record ChromeLaunchOptions(
    string? ExecutablePath = null,
    bool Headless = true,
    TimeSpan? StartupTimeout = null);

/// <summary>
/// Cross-platform headless Chromium process manager. Starts Chrome with a
/// random remote-debugging port, captures the WebSocket debugger URL printed
/// to stderr, and exposes both for <see cref="CdpConnection"/>.
/// </summary>
public sealed class ChromeLauncher : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _userDataDir;
    private int _disposed;

    public Uri DebuggerWebSocketUri { get; }

    private ChromeLauncher(Process process, Uri wsUri, string userDataDir)
    {
        _process = process;
        DebuggerWebSocketUri = wsUri;
        _userDataDir = userDataDir;
    }

    public static async Task<ChromeLauncher> LaunchAsync(
        ChromeLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var executable = options.ExecutablePath ?? ResolveExecutable()
            ?? throw new CdpException("Could not locate a Chromium-based browser. Set ChromeLaunchOptions.ExecutablePath.");

        var userDataDir = Path.Combine(Path.GetTempPath(), "nativebeam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userDataDir);

        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(options.Headless ? "--headless=new" : "--headless=old");
        psi.ArgumentList.Add("--disable-gpu");
        psi.ArgumentList.Add("--no-first-run");
        psi.ArgumentList.Add("--no-default-browser-check");
        psi.ArgumentList.Add("--disable-extensions");
        psi.ArgumentList.Add("--disable-dev-shm-usage");
        psi.ArgumentList.Add("--remote-debugging-port=0");
        psi.ArgumentList.Add($"--user-data-dir={userDataDir}");
        psi.ArgumentList.Add("about:blank");

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var wsTcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }
            const string marker = "DevTools listening on ";
            var idx = e.Data.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                return;
            }
            var url = e.Data[(idx + marker.Length)..].Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                wsTcs.TrySetResult(uri);
            }
        };
        process.Exited += (_, _) =>
        {
            wsTcs.TrySetException(new CdpException($"Chrome exited (code {process.ExitCode}) before reporting the debugger URL."));
        };

        if (!process.Start())
        {
            TryDelete(userDataDir);
            throw new CdpException("Failed to start the Chromium process.");
        }
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        var timeout = options.StartupTimeout ?? TimeSpan.FromSeconds(15);
        Uri wsUri;
        try
        {
            wsUri = await wsTcs.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryKill(process);
            TryDelete(userDataDir);
            throw;
        }

        return new ChromeLauncher(process, wsUri, userDataDir);
    }

    private static string? ResolveExecutable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return FirstExisting(
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                "/Applications/Chromium.app/Contents/MacOS/Chromium",
                "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge");
        }

        // Linux / others
        return FirstExisting(
            "/usr/bin/google-chrome",
            "/usr/bin/google-chrome-stable",
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            "/snap/bin/chromium",
            "/usr/bin/microsoft-edge");
    }

    private static string? FirstExisting(params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrEmpty(c) && File.Exists(c))
            {
                return c;
            }
        }
        return null;
    }

    private static void TryKill(Process p)
    {
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        TryKill(_process);

        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
        TryDelete(_userDataDir);
    }
}
