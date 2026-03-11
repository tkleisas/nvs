using System.Diagnostics;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.Services.Lsp;

public sealed class LanguageServerManager : ILanguageServerManager
{
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

        var path = FindBinaryOnPath(def.BinaryName);
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
            InstallMethod.BinaryDownload => HandleBinaryDownload(def, progress),
            _ => false,
        };
    }

    public string? FindServerBinary(string serverId)
    {
        var def = LanguageServerRegistry.GetById(serverId);
        return def is null ? null : FindBinaryOnPath(def.BinaryName);
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
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar is null)
            return null;

        var paths = pathVar.Split(Path.PathSeparator);
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var dir in paths)
        {
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
