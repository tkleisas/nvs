namespace NVS.Services.Debug;

/// <summary>
/// Registry mapping debug adapter types to their executables and arguments.
/// </summary>
public sealed class DebugAdapterRegistry
{
    private readonly Dictionary<string, DebugAdapterInfo> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private readonly DebugAdapterDownloader _downloader;

    public DebugAdapterRegistry() : this(new DebugAdapterDownloader()) { }

    public DebugAdapterRegistry(DebugAdapterDownloader downloader)
    {
        _downloader = downloader;

        // Register built-in adapters
        Register(new DebugAdapterInfo
        {
            Type = "coreclr",
            DisplayName = ".NET (netcoredbg)",
            ExecutableName = "netcoredbg",
            Arguments = ["--interpreter=vscode"],
            SupportedRuntimes = ["dotnet"],
        });
    }

    public DebugAdapterDownloader Downloader => _downloader;

    public void Register(DebugAdapterInfo adapter)
    {
        _adapters[adapter.Type] = adapter;
    }

    public DebugAdapterInfo? GetAdapter(string type) =>
        _adapters.TryGetValue(type, out var adapter) ? adapter : null;

    public IReadOnlyList<DebugAdapterInfo> GetAllAdapters() => [.. _adapters.Values];

    /// <summary>
    /// Attempts to find the adapter executable on PATH, common locations, or the NVS tools directory.
    /// Returns the full path if found, null otherwise.
    /// </summary>
    public string? FindAdapterExecutable(string type, string? customPath = null)
    {
        if (customPath is not null && File.Exists(customPath))
            return customPath;

        var adapter = GetAdapter(type);
        if (adapter is null) return null;

        var executableName = adapter.ExecutableName;
        if (OperatingSystem.IsWindows())
            executableName += ".exe";

        // Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in pathDirs)
        {
            var fullPath = Path.Combine(dir, executableName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        // Check common locations
        var commonPaths = GetCommonAdapterPaths(executableName);
        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Check NVS tools directory (auto-downloaded adapters)
        var toolsPath = _downloader.GetInstalledPath(adapter.ExecutableName);
        if (toolsPath is not null)
            return toolsPath;

        return null;
    }

    private static IEnumerable<string> GetCommonAdapterPaths(string executableName)
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, "netcoredbg", executableName);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "netcoredbg", executableName);
        }
        else
        {
            yield return Path.Combine("/usr", "local", "bin", executableName);
            yield return Path.Combine("/usr", "bin", executableName);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".dotnet", "tools", executableName);
            yield return Path.Combine(home, ".local", "bin", executableName);
        }
    }
}

public sealed record DebugAdapterInfo
{
    public required string Type { get; init; }
    public required string DisplayName { get; init; }
    public required string ExecutableName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IReadOnlyList<string> SupportedRuntimes { get; init; } = [];
}
