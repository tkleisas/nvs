using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NVS.Services.Debug;

/// <summary>
/// Downloads and installs debug adapter binaries on demand.
/// Stores them in ~/.nvs/tools/{adapter}/.
/// </summary>
public sealed class DebugAdapterDownloader
{
    // Latest known netcoredbg release. When bumping the version, the artifact
    // checksums below must be recomputed for the new release.
    private const string NetcoredbgVersion = "3.1.3-1062";
    private const string NetcoredbgBaseUrl = "https://github.com/Samsung/netcoredbg/releases/download";

    // SHA-256 of each release artifact, verified after download and before
    // extraction — catches truncated downloads and tampered binaries.
    private static readonly Dictionary<string, string> NetcoredbgSha256 = new()
    {
        ["netcoredbg-win64.zip"] = "c67ae052e0bcb9ce37000f261e2d397a0d5b6615cafe30c868239a78598dfb37",
        ["netcoredbg-osx-amd64.tar.gz"] = "49459b066836b6a452f418501d7ecab57bcd7e60d8464faac21ff70b496b8634",
        ["netcoredbg-linux-amd64.tar.gz"] = "3814341c028c81ff7eea03ac316ad92e9ad7d705d2a00e3e3df269cdc241c763",
        ["netcoredbg-linux-arm64.tar.gz"] = "fc9efb691a53932a7fac4b9f67af68ad0c2a4cbe59cb2c1a3c44c64959df2ba4",
    };

    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    private readonly string _toolsDir;

    public DebugAdapterDownloader(string? toolsDir = null)
    {
        _toolsDir = toolsDir ?? GetDefaultToolsDir();
    }

    public string ToolsDirectory => _toolsDir;

    /// <summary>
    /// Returns the path to the netcoredbg executable if already installed locally.
    /// </summary>
    public string? GetInstalledPath(string adapterName)
    {
        var executableName = adapterName;
        if (OperatingSystem.IsWindows())
            executableName += ".exe";

        var path = Path.Combine(_toolsDir, adapterName, executableName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Downloads and extracts netcoredbg for the current platform.
    /// Returns the full path to the executable.
    /// </summary>
    public async Task<string> EnsureNetcoredbgAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var existing = GetInstalledPath("netcoredbg");
        if (existing is not null)
            return existing;

        var (url, archiveExt) = GetNetcoredbgDownloadUrl();
        var destDir = Path.Combine(_toolsDir, "netcoredbg");
        Directory.CreateDirectory(destDir);

        var tempFile = Path.Combine(Path.GetTempPath(), $"netcoredbg-{Guid.NewGuid()}{archiveExt}");

        try
        {
            // Download
            progress?.Report($"Downloading netcoredbg {NetcoredbgVersion}...");
            await DownloadFileAsync(url, tempFile, progress, cancellationToken).ConfigureAwait(false);

            // Verify integrity before executing anything from the archive
            progress?.Report("Verifying download...");
            await VerifyChecksumAsync(tempFile, GetExpectedSha256(url), cancellationToken).ConfigureAwait(false);

            // Extract
            progress?.Report("Extracting netcoredbg...");
            await ExtractArchiveAsync(tempFile, destDir, archiveExt, cancellationToken).ConfigureAwait(false);

            // Set executable permissions on Unix
            if (!OperatingSystem.IsWindows())
            {
                var execPath = Path.Combine(destDir, "netcoredbg");
                if (File.Exists(execPath))
                    File.SetUnixFileMode(execPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                                  | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                                                  | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            var result = GetInstalledPath("netcoredbg")
                ?? throw new InvalidOperationException("netcoredbg was downloaded but executable not found after extraction.");

            progress?.Report("netcoredbg installed successfully.");
            return result;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* cleanup best effort */ }
        }
    }

    internal static (string Url, string Extension) GetNetcoredbgDownloadUrl()
    {
        var arch = RuntimeInformation.OSArchitecture;

        if (OperatingSystem.IsWindows())
        {
            return ($"{NetcoredbgBaseUrl}/{NetcoredbgVersion}/netcoredbg-win64.zip", ".zip");
        }

        if (OperatingSystem.IsMacOS())
        {
            return ($"{NetcoredbgBaseUrl}/{NetcoredbgVersion}/netcoredbg-osx-amd64.tar.gz", ".tar.gz");
        }

        // Linux
        var linuxArch = arch switch
        {
            Architecture.Arm64 => "arm64",
            _ => "amd64",
        };

        return ($"{NetcoredbgBaseUrl}/{NetcoredbgVersion}/netcoredbg-linux-{linuxArch}.tar.gz", ".tar.gz");
    }

    /// <summary>Returns the pinned SHA-256 for a release artifact URL.</summary>
    internal static string GetExpectedSha256(string url)
    {
        var fileName = url[(url.LastIndexOf('/') + 1)..];
        return NetcoredbgSha256.TryGetValue(fileName, out var expected)
            ? expected
            : throw new InvalidOperationException($"No pinned checksum for '{fileName}'.");
    }

    /// <summary>Throws when the file's SHA-256 does not match the expected value.</summary>
    internal static async Task VerifyChecksumAsync(
        string filePath,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(hashBytes);

        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Checksum mismatch for '{Path.GetFileName(filePath)}': expected {expectedSha256}, got {actual}. " +
                "The download may be corrupt or tampered with; it has been discarded.");
        }
    }

    private static async Task DownloadFileAsync(
        string url,
        string destPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SharedHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = File.Create(destPath);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                var pct = (int)(totalRead * 100 / totalBytes.Value);
                progress?.Report($"Downloading netcoredbg... {pct}%");
            }
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destDir,
        string extension,
        CancellationToken cancellationToken)
    {
        if (extension == ".zip")
        {
            await Task.Run(() => ExtractZip(archivePath, destDir), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await Task.Run(() => ExtractTarGz(archivePath, destDir), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ExtractZip(string archivePath, string destDir)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            // Archives contain a netcoredbg/ prefix — flatten it
            var relativePath = StripFirstDirectory(entry.FullName);
            var targetPath = Path.Combine(destDir, relativePath);

            var targetDir = Path.GetDirectoryName(targetPath);
            if (targetDir is not null)
                Directory.CreateDirectory(targetDir);

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static void ExtractTarGz(string archivePath, string destDir)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var tarReader = new TarReader(gzipStream);

        while (tarReader.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                continue;

            var relativePath = StripFirstDirectory(entry.Name);
            if (string.IsNullOrEmpty(relativePath))
                continue;

            var targetPath = Path.Combine(destDir, relativePath);

            var targetDir = Path.GetDirectoryName(targetPath);
            if (targetDir is not null)
                Directory.CreateDirectory(targetDir);

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    /// <summary>
    /// Strips the first directory from a path (e.g., "netcoredbg/foo.dll" → "foo.dll").
    /// </summary>
    internal static string StripFirstDirectory(string entryPath)
    {
        var normalized = entryPath.Replace('\\', '/');
        var slashIndex = normalized.IndexOf('/');
        return slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
    }

    private static string GetDefaultToolsDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".nvs", "tools");
    }
}
