using System.Text.Json;
using System.Text.Json.Nodes;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.Services.Settings;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private AppSettings _appSettings = new();
    private WorkspaceSettings? _workspaceSettings;
    private readonly string _appSettingsPath;

    public AppSettings AppSettings => _appSettings;
    public WorkspaceSettings? WorkspaceSettings => _workspaceSettings;

    public event EventHandler<AppSettings>? AppSettingsChanged;
    public event EventHandler<WorkspaceSettings>? WorkspaceSettingsChanged;

    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var nvsPath = Path.Combine(appDataPath, "NVS");
        Directory.CreateDirectory(nvsPath);
        _appSettingsPath = Path.Combine(nvsPath, "settings.json");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _appSettings = await LoadAppSettingsAsync(cancellationToken);
    }

    public async Task<AppSettings> LoadAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_appSettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_appSettingsPath, cancellationToken);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAppSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _appSettings = settings;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_appSettingsPath, json, cancellationToken);
        AppSettingsChanged?.Invoke(this, settings);
    }

    public async Task<WorkspaceSettings> LoadWorkspaceSettingsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var settingsPath = Path.Combine(workspacePath, ".nvs", "workspace.json");
        
        if (!File.Exists(settingsPath))
        {
            return new WorkspaceSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(settingsPath, cancellationToken);
            _workspaceSettings = JsonSerializer.Deserialize<WorkspaceSettings>(json, JsonOptions) ?? new WorkspaceSettings();
            return _workspaceSettings;
        }
        catch
        {
            return new WorkspaceSettings();
        }
    }

    public async Task SaveWorkspaceSettingsAsync(string workspacePath, WorkspaceSettings settings, CancellationToken cancellationToken = default)
    {
        var nvsPath = Path.Combine(workspacePath, ".nvs");
        Directory.CreateDirectory(nvsPath);
        
        var settingsPath = Path.Combine(nvsPath, "workspace.json");
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(settingsPath, json, cancellationToken);
        
        _workspaceSettings = settings;
        WorkspaceSettingsChanged?.Invoke(this, settings);
    }

    public T Get<T>(string key, T defaultValue)
    {
        var value = _appSettings.Properties.TryGetValue(key, out var obj) ? obj : null;
        if (value is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions) ?? defaultValue;
        }
        return value is T typedValue ? typedValue : defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        _appSettings = _appSettings with { Properties = new Dictionary<string, object>(_appSettings.Properties) { [key] = value! } };
    }
}
