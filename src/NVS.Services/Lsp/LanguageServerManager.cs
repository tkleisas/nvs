using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.Services.Lsp;

public sealed class LanguageServerManager : ILanguageServerManager
{
    private static readonly HttpClient SharedHttpClient = new();

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
            InstallMethod.BinaryDownload => HandleBinaryDownload(def, progress),
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

        var ext = OperatingSystem.IsWindows() ? "zip" : "tar.gz";
        var url = def.DownloadUrlTemplate
            .Replace("{version}", def.Version)
            .Replace("{rid}", rid)
            .Replace("{ext}", ext);

        var toolsDir = GetNvsToolsDir(def.Id);
        Directory.CreateDirectory(toolsDir);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{def.Id}-{rid}.{ext}");

        try
        {
            progress?.Report($"Downloading {def.Name} {def.Version} for {rid}...");

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
        var tarPath = FindBinaryOnPath("tar");
        if (tarPath is null)
            throw new InvalidOperationException("'tar' is not available on PATH. Cannot extract .tar.gz archive.");

        var psi = new ProcessStartInfo
        {
            FileName = tarPath,
            ArgumentList = { "xzf", archivePath, "-C", destinationDir },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start tar process.");

        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"tar extraction failed: {error}");
        }
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
