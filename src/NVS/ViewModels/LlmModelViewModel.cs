using System.ComponentModel;
using System.Runtime.CompilerServices;
using NVS.Core.LLM;

namespace NVS.ViewModels;

public sealed class LlmModelViewModel : INotifyPropertyChanged
{
    private string _displayName = "";
    private string _modelId = "";
    private string _hostUrl = "";
    private string _completionsPath = "";
    private string _authToken = "";
    private string _authScheme = "Bearer";
    private bool _enabled = true;
    private bool _supportsTools = true;
    private double _temperature;
    private int _maxOutputTokens = 4096;
    private int _httpTimeoutSeconds;
    private bool _isEditing;

    public string DisplayName { get => _displayName; set => Set(ref _displayName, value); }
    public string ModelId { get => _modelId; set => Set(ref _modelId, value); }
    public string HostUrl { get => _hostUrl; set => Set(ref _hostUrl, value); }
    public string CompletionsPath { get => _completionsPath; set => Set(ref _completionsPath, value); }
    public string AuthToken { get => _authToken; set => Set(ref _authToken, value); }
    public string AuthScheme { get => _authScheme; set => Set(ref _authScheme, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool SupportsTools { get => _supportsTools; set => Set(ref _supportsTools, value); }
    public double Temperature { get => _temperature; set => Set(ref _temperature, value); }
    public int MaxOutputTokens { get => _maxOutputTokens; set => Set(ref _maxOutputTokens, value); }
    public int HttpTimeoutSeconds { get => _httpTimeoutSeconds; set => Set(ref _httpTimeoutSeconds, value); }
    public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ToggleEdit() => IsEditing = !IsEditing;

    public static LlmModelViewModel FromConfig(LlmModelConfig cfg) => new()
    {
        _displayName = cfg.DisplayName,
        _modelId = cfg.ModelId,
        _hostUrl = cfg.HostUrl,
        _completionsPath = cfg.CompletionsPath,
        _authToken = cfg.AuthToken,
        _authScheme = cfg.AuthScheme,
        _enabled = cfg.Enabled,
        _supportsTools = cfg.SupportsTools,
        _temperature = cfg.Temperature,
        _maxOutputTokens = cfg.MaxOutputTokens,
        _httpTimeoutSeconds = cfg.HttpTimeoutSeconds,
    };

    public LlmModelConfig ToConfig() => new()
    {
        DisplayName = _displayName,
        ModelId = _modelId,
        HostUrl = _hostUrl,
        CompletionsPath = _completionsPath,
        AuthToken = _authToken,
        AuthScheme = _authScheme,
        Enabled = _enabled,
        SupportsTools = _supportsTools,
        Temperature = _temperature,
        MaxOutputTokens = _maxOutputTokens,
        HttpTimeoutSeconds = _httpTimeoutSeconds,
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}