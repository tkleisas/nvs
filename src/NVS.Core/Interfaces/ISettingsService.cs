using NVS.Core.Models.Settings;

namespace NVS.Core.Interfaces;

public interface ISettingsService
{
    AppSettings AppSettings { get; }
    WorkspaceSettings? WorkspaceSettings { get; }
    
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<AppSettings> LoadAppSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveAppSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<WorkspaceSettings> LoadWorkspaceSettingsAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task SaveWorkspaceSettingsAsync(string workspacePath, WorkspaceSettings settings, CancellationToken cancellationToken = default);
    
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
    
    event EventHandler<AppSettings>? AppSettingsChanged;
    event EventHandler<WorkspaceSettings>? WorkspaceSettingsChanged;
}
