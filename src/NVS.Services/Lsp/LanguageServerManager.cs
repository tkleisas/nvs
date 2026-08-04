using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.Services.Lsp;

public sealed class LanguageServerManager : ILanguageServerManager
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        // Language server archives can be large (jdtls ~50 MB) and mirrors slow —
        // the default 100s timeout is not enough.
        Timeout = TimeSpan.FromMinutes(15),
    };

    public IReadOnlyList<LanguageServerDefinition> GetAvailableServers() =>
        LanguageServerRegistry.GetAll();

    public LanguageServerDefinition? GetServerForLanguage(Language language) =>
        LanguageServerRegistry.GetForLanguage(language);

    public async Task<LanguageServerStatus> CheckServerStatusAsync(
        string serverId,
        CancellationToken cancellationToken = default)
    {
        var def = LanguageServerRegistry.GetById(serverId);
        if (def is null)
            return LanguageServerStatus.Unknown;

        var path = FindBinaryOnPath(def.BinaryName) ?? FindInNvsTools(serverId, def.BinaryName);
        return await Task.FromResult(path is not null
            ? LanguageServerStatus.Installed
            : LanguageServerStatus.NotInstalled);
    }

    public async Task<bool> InstallServerAsync(
        string serverId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var def = LanguageServerRegistry.GetById(serverId);
        if (def is null)
        {
            progress?.Report($"Unknown server: {serverId}");
            return false;
        }

        return def.InstallMethod switch
        {
            InstallMethod.Npm => await RunInstallCommandAsync(
                "npm", $"install -g {def.InstallPackage}", def, progress, cancellationToken),
            InstallMethod.Pip => await RunInstallCommandAsync(
                "pip", $"install {def.InstallPackage}", def, progress, cancellationToken),
            InstallMethod.DotnetTool => await RunInstallCommandAsync(
                "dotnet", $"tool install -g {def.InstallPackage}", def, progress, cancellationToken),
            InstallMethod.Cargo => await RunInstallCommandAsync(
                "cargo", $"install {def.InstallPackage}", def, progress, cancellationToken),
            InstallMethod.GoInstall => await RunInstallCommandAsync(
                "go", $"install {def.InstallPackage}", def, progress, cancellationToken),
            InstallMethod.GitHubRelease => await DownloadAndExtractAsync(def, progress, cancellationToken),
            // BinaryDownload with a URL template downloads and extracts; without
            // one it falls back to the manual-download hint.
            InstallMethod.BinaryDownload => string.IsNullOrEmpty(def.DownloadUrlTemplate)
                ? HandleBinaryDownload(def, progress)
                : await DownloadAndExtractAsync(def, progress, cancellationToken),
            _ => false,
        };
    }

    public string? FindServerBinary(string serverId)
    {
        var def = LanguageServerRegistry.GetById(serverId);
        if (def is null) return null;
        return FindBinaryOnPath(def.BinaryName) ?? FindInNvsTools(serverId, def.BinaryName);
    }

    private static async Task<bool> DownloadAndExtractAsync(
        LanguageServerDefinition def,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(def.DownloadUrlTemplate) || string.IsNullOrEmpty(def.Version))
        {
            progress?.Report($"No download URL configured for {def.Name}.");
            return false;
        }

        var rid = GetCurrentRid();
        if (rid is null)
        {
            progress?.Report($"Unsupported platform for {def.Name} auto-download.");
            return false;
        }

        var (url, ext) = ResolveDownloadUrl(def, rid);

        var toolsDir = GetNvsToolsDir(def.Id);
        Directory.CreateDirectory(toolsDir);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{def.Id}-{rid}.{ext}");

        try
        {
            progress?.Report($"Downloading {def.Name} {def.Version} for {rid}...");

            // Large archives on slow/flaky mirrors (eclipse.org) can reset mid-way —
            // retry the download a few times before giving up.
            const int maxAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using (var response = await SharedHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength;
                        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                        var buffer = new byte[81920];
                        long downloaded = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                            downloaded += bytesRead;

                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                var pct = (int)(downloaded * 100 / totalBytes.Value);
                                progress?.Report($"Downloading {def.Name}... {pct}%");
                            }
                        }
                    }
                    break;
                }
                catch (Exception ex) when (attempt < maxAttempts && ex is not OperationCanceledException)
                {
                    progress?.Report($"Download interrupted ({ex.GetType().Name}); retrying ({attempt}/{maxAttempts - 1})...");
                    try { File.Delete(tempFile); } catch { /* partial file cleanup */ }
                }
            }

            progress?.Report($"Extracting {def.Name}...");

            if (ext == "zip")
            {
                ZipFile.ExtractToDirectory(tempFile, toolsDir, overwriteFiles: true);
            }
            else
            {
                // Use tar for .tar.gz extraction
                await ExtractTarGzAsync(tempFile, toolsDir, cancellationToken);
            }

            if (def.Id == "jdtls")
            {
                // JDT.LS ships no standalone executable — generate the launcher
                // script the factory looks for (resolves a JDK at run time).
                WriteJdtlsLauncher(toolsDir);
            }

            // Make binary executable on Unix
            if (!OperatingSystem.IsWindows())
            {
                var binaryPath = Path.Combine(toolsDir, def.BinaryName);
                if (File.Exists(binaryPath))
                {
                    await RunCommandAsync("chmod", $"+x \"{binaryPath}\"", cancellationToken);
                }
            }

            progress?.Report($"Successfully installed {def.Name} {def.Version}.");
            return true;
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Download cancelled.");
            return false;
        }
        catch (HttpRequestException ex)
        {
            progress?.Report($"Download failed: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            progress?.Report($"Install error: {ex.Message}");
            return false;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); }
                catch { /* best effort cleanup */ }
            }
        }
    }

    internal static string? GetCurrentRid()
    {
        if (OperatingSystem.IsWindows())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        if (OperatingSystem.IsLinux())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        return null;
    }

    internal static string GetNvsToolsDir(string serverId) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NVS", "tools", serverId);

    /// <summary>
    /// Builds the final download URL and derives the archive format from it, so
    /// servers with a single archive kind (e.g. jdtls ships only .tar.gz) need
    /// no per-OS {ext} placeholder.
    /// </summary>
    internal static (string Url, string Ext) ResolveDownloadUrl(LanguageServerDefinition def, string rid)
    {
        var url = def.DownloadUrlTemplate!
            .Replace("{version}", def.Version)
            .Replace("{rid}", rid);

        if (url.Contains("{ext}", StringComparison.Ordinal))
        {
            url = url.Replace("{ext}", OperatingSystem.IsWindows() ? "zip" : "tar.gz");
        }

        var ext = url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "zip" : "tar.gz";
        return (url, ext);
    }

    /// <summary>Writes the jdtls launcher script the LSP factory looks for in the tools dir.</summary>
    internal static void WriteJdtlsLauncher(string toolsDir)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(toolsDir, "jdtls.cmd"), JdtlsCmdContent.Replace("\n", "\r\n"));
        }
        else
        {
            var path = Path.Combine(toolsDir, "jdtls");
            File.WriteAllText(path, JdtlsShContent);
        }
    }

    internal const string JdtlsCmdContent = """
        @echo off
        setlocal
        rem JDT.LS launcher generated by NVS. Resolves a JDK 17+ at run time.
        rem Order: user apps JDKs (newest) -> MS OpenJDK dirs (newest) -> JAVA_HOME -> PATH.
        set "JAVA_BIN="
        if not defined JAVA_BIN (
          for /f "delims=" %%J in ('dir /b /ad /o-n "%USERPROFILE%\apps\jdk-*" 2^>nul') do (
            if not defined JAVA_BIN if exist "%USERPROFILE%\apps\%%J\bin\java.exe" set "JAVA_BIN=%USERPROFILE%\apps\%%J\bin\java.exe"
          )
        )
        if not defined JAVA_BIN (
          for /f "delims=" %%J in ('dir /b /ad /o-n "%ProgramFiles%\Microsoft\jdk-*" 2^>nul') do (
            if not defined JAVA_BIN if exist "%ProgramFiles%\Microsoft\%%J\bin\java.exe" set "JAVA_BIN=%ProgramFiles%\Microsoft\%%J\bin\java.exe"
          )
        )
        if not defined JAVA_BIN if defined JAVA_HOME if exist "%JAVA_HOME%\bin\java.exe" set "JAVA_BIN=%JAVA_HOME%\bin\java.exe"
        if not defined JAVA_BIN (
          where java >nul 2>nul && set "JAVA_BIN=java"
        )
        if not defined JAVA_BIN (
          echo JDT.LS requires a JDK 17+. Install one or point JAVA_HOME at it. 1>&2
          exit /b 1
        )
        for %%f in ("%~dp0plugins\org.eclipse.equinox.launcher_*.jar") do set "LAUNCHER=%%f"
        "%JAVA_BIN%" -Declipse.application=org.eclipse.jdt.ls.core.id1 -Dosgi.bundles.defaultStartLevel=4 -Declipse.product=org.eclipse.jdt.ls.core.product -Dlog.level=ERROR -Xmx1G --add-modules=ALL-SYSTEM --add-opens java.base/java.util=ALL-UNNAMED --add-opens java.base/java.lang=ALL-UNNAMED -jar "%LAUNCHER%" -configuration "%~dp0config_win" -data "%TEMP%\nvs-jdtls-data" %*
        endlocal

        """;

    internal const string JdtlsShContent = """
        #!/bin/sh
        # JDT.LS launcher generated by NVS. Resolves a JDK 17+ at run time.
        # Order: user apps JDKs (newest) -> JAVA_HOME -> PATH.
        DIR="$(cd "$(dirname "$0")" && pwd)"
        JAVA_BIN=""
        for j in "$HOME"/apps/jdk-*/bin/java; do
          [ -x "$j" ] && JAVA_BIN="$j"
        done
        if [ -z "$JAVA_BIN" ] && [ -n "$JAVA_HOME" ] && [ -x "$JAVA_HOME/bin/java" ]; then
          JAVA_BIN="$JAVA_HOME/bin/java"
        fi
        if [ -z "$JAVA_BIN" ] && command -v java >/dev/null 2>&1; then
          JAVA_BIN="java"
        fi
        if [ -z "$JAVA_BIN" ]; then
          echo "JDT.LS requires a JDK 17+. Install one or point JAVA_HOME at it." >&2
          exit 1
        fi
        LAUNCHER=$(ls "$DIR"/plugins/org.eclipse.equinox.launcher_*.jar | head -n 1)
        exec "$JAVA_BIN" -Declipse.application=org.eclipse.jdt.ls.core.id1 -Dosgi.bundles.defaultStartLevel=4 -Declipse.product=org.eclipse.jdt.ls.core.product -Dlog.level=ERROR -Xmx1G --add-modules=ALL-SYSTEM --add-opens java.base/java.util=ALL-UNNAMED --add-opens java.base/java.lang=ALL-UNNAMED -jar "$LAUNCHER" -configuration "$DIR/config_linux" -data "${TMPDIR:-/tmp}/nvs-jdtls-data" "$@"

        """;

    internal static string? FindInNvsTools(string serverId, string binaryName)
    {
        var toolsDir = GetNvsToolsDir(serverId);
        if (!Directory.Exists(toolsDir))
            return null;

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var ext in extensions)
        {
            var fullPath = Path.Combine(toolsDir, binaryName + ext);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    private static async Task ExtractTarGzAsync(string archivePath, string destinationDir, CancellationToken ct)
    {
        // Managed extraction (System.Formats.Tar) — no external 'tar' dependency
        // and no MSYS-vs-native path issues on Windows.
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
        await Task.Run(() => TarFile.ExtractToDirectory(gzip, destinationDir, overwriteFiles: true), ct)
            .ConfigureAwait(false);
    }

    private static async Task RunCommandAsync(string command, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is not null)
            await process.WaitForExitAsync(ct);
    }

    private static async Task<bool> RunInstallCommandAsync(
        string command,
        string arguments,
        LanguageServerDefinition def,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var toolPath = FindBinaryOnPath(command);
        if (toolPath is null)
        {
            progress?.Report($"'{command}' is not installed or not on PATH. Please install it first.");
            return false;
        }

        progress?.Report($"Installing {def.Name} via '{command} {arguments}'...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                progress?.Report("Failed to start install process.");
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                progress?.Report($"Successfully installed {def.Name}.");
                return true;
            }

            progress?.Report($"Install failed (exit code {process.ExitCode}): {error}");
            return false;
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Installation cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            progress?.Report($"Install error: {ex.Message}");
            return false;
        }
    }

    private static bool HandleBinaryDownload(LanguageServerDefinition def, IProgress<string>? progress)
    {
        progress?.Report(
            $"{def.Name} must be downloaded manually. Visit: {def.HomepageUrl}");
        return false;
    }

    internal static string? FindBinaryOnPath(string binaryName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        var searchDirs = new List<string>(pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        // On non-Windows, common tool install directories may not be in $PATH
        // (especially when launched from a desktop shortcut rather than a shell).
        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                searchDirs.Add(Path.Combine(home, ".dotnet", "tools"));
                searchDirs.Add(Path.Combine(home, ".local", "bin"));
                searchDirs.Add(Path.Combine(home, ".cargo", "bin"));
                searchDirs.Add(Path.Combine(home, "go", "bin"));
            }
        }

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir))
                continue;

            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(dir, binaryName + ext);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }
}
