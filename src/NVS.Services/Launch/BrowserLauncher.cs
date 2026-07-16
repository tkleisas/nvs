using System.Diagnostics;
using NVS.Core.Interfaces;
using Serilog;

namespace NVS.Services.Launch;

/// <summary>
/// Cross-platform <see cref="IBrowserLauncher"/>. Uses shell-execute on Windows,
/// <c>open</c> on macOS and <c>xdg-open</c> elsewhere (Linux/BSD).
/// </summary>
public sealed class BrowserLauncher : IBrowserLauncher
{
    public bool Launch(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                // Shell-execute the URL directly — Windows opens it with the default
                // handler (browser). Avoids the `cmd /c start` window-title gotcha
                // where the first quoted arg is treated as a title, not the URL.
                psi = new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                    CreateNoWindow = true,
                };
            }
            else
            {
                var (fileName, args) = ResolveOpenCommand(url);
                if (fileName is null) return false;
                psi = new ProcessStartInfo(fileName, args)
                {
                    UseShellExecute = true,
                    CreateNoWindow = true,
                };
            }

            using var proc = Process.Start(psi);
            Log.Information("[BrowserLauncher] opened {Url}", url);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[BrowserLauncher] failed to open {Url}", url);
            return false;
        }
    }

    /// <summary>Exposed for unit testing: resolves the shell command tuple for a URL (non-Windows).</summary>
    internal static (string? FileName, string Args) ResolveOpenCommand(string url)
    {
        // Escaping: wrap the URL in quotes so query strings / fragments don't trip the shell.
        var quoted = "\"" + url.Replace("\"", "\\\"") + "\"";

        if (OperatingSystem.IsMacOS())
            return ("open", quoted);

        // Linux / others: prefer xdg-open, fall back to sensible-browser.
        return ("xdg-open", quoted);
    }
}