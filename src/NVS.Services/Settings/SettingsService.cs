using System.Text.Json;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using Serilog;

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

    public SettingsService(string? appSettingsPath = null)
    {
        if (appSettingsPath is null)
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var nvsPath = Path.Combine(appDataPath, "NVS");
            Directory.CreateDirectory(nvsPath);
            appSettingsPath = Path.Combine(nvsPath, "settings.json");
        }
        else
        {
            var directory = Path.GetDirectoryName(appSettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        _appSettingsPath = appSettingsPath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _appSettings = await LoadAppSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppSettings> LoadAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await TryLoadSettingsFileAsync<AppSettings>(_appSettingsPath, cancellationToken).ConfigureAwait(false);
        return loaded ?? new AppSettings();
    }

    public async Task SaveAppSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _appSettings = settings;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await WriteFileAtomicAsync(_appSettingsPath, json, cancellationToken).ConfigureAwait(false);
        AppSettingsChanged?.Invoke(this, settings);
    }

    public async Task<WorkspaceSettings> LoadWorkspaceSettingsAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var settingsPath = Path.Combine(workspacePath, ".nvs", "workspace.json");

        var loaded = await TryLoadSettingsFileAsync<WorkspaceSettings>(settingsPath, cancellationToken).ConfigureAwait(false);
        if (loaded is null)
        {
            return new WorkspaceSettings();
        }

        _workspaceSettings = loaded;
        return loaded;
    }

    public async Task SaveWorkspaceSettingsAsync(string workspacePath, WorkspaceSettings settings, CancellationToken cancellationToken = default)
    {
        var nvsPath = Path.Combine(workspacePath, ".nvs");
        Directory.CreateDirectory(nvsPath);

        var settingsPath = Path.Combine(nvsPath, "workspace.json");
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await WriteFileAtomicAsync(settingsPath, json, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Loads and deserializes a settings file. Returns null when the file is missing
    /// or unreadable. A file that exists but fails to parse is backed up to
    /// "&lt;file&gt;.bak" first, so a later save cannot silently destroy the user's
    /// only copy of their settings.
    /// </summary>
    private static async Task<T?> TryLoadSettingsFileAsync<T>(string path, CancellationToken cancellationToken)
        where T : class, new()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            BackupCorruptFile(path, ex);
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read settings file {Path}; using defaults", path);
            return null;
        }
    }

    private static void BackupCorruptFile(string path, Exception cause)
    {
        var backupPath = path + ".bak";
        try
        {
            File.Copy(path, backupPath, overwrite: true);
            Log.Warning(cause, "Settings file {Path} is corrupt; backed it up to {BackupPath} and reset to defaults", path, backupPath);
        }
        catch (Exception copyEx)
        {
            Log.Warning(copyEx, "Settings file {Path} is corrupt and could not be backed up; resetting to defaults", path);
        }
    }

    /// <summary>
    /// Writes via a temp file and an atomic rename so a crash mid-write can never
    /// leave a truncated settings file behind.
    /// </summary>
    private static async Task WriteFileAtomicAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the stray .tmp file is harmless.
            }
            throw;
        }
    }
}
