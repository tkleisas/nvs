namespace NVS.Core.Interfaces;

public interface IPluginManager
{
    IReadOnlyList<PluginInfo> LoadedPlugins { get; }
    bool IsLoading { get; }
    
    Task LoadPluginAsync(string path, CancellationToken cancellationToken = default);
    Task UnloadPluginAsync(string pluginId, CancellationToken cancellationToken = default);
    Task ReloadPluginAsync(string pluginId, CancellationToken cancellationToken = default);
    Task LoadAllPluginsAsync(string pluginsDirectory, CancellationToken cancellationToken = default);
    
    T? GetService<T>() where T : class;
    object? GetService(Type serviceType);
    
    event EventHandler<PluginInfo>? PluginLoaded;
    event EventHandler<PluginInfo>? PluginUnloaded;
    event EventHandler<PluginErrorEventArgs>? PluginError;
}

public sealed record PluginInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public string? Author { get; init; }
    public string? MainAssemblyPath { get; init; }
    public bool IsEnabled { get; init; } = true;
}

public sealed class PluginErrorEventArgs : EventArgs
{
    public required string PluginId { get; init; }
    public required string Message { get; init; }
    public Exception? Exception { get; init; }
}
