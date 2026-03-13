using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.ViewModels;

public partial class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly ILanguageServerManager _serverManager;
    private AppSettings _settings;

    private int _selectedSectionIndex;

    // General
    private bool _restorePreviousSession;
    private bool _checkUpdatesOnStartup;

    // Editor
    private int _fontSize;
    private string _fontFamily = "JetBrains Mono";
    private int _tabSize;
    private bool _insertSpaces;
    private bool _wordWrap;
    private bool _lineNumbers;
    private bool _highlightCurrentLine;
    private bool _showWhitespace;
    private bool _autoSave;
    private int _autoSaveDelay;

    // Terminal
    private string _terminalFontFamily = "Cascadia Mono,Cascadia Code,Consolas,Courier New,monospace";
    private int _terminalFontSize = 14;
    private int _terminalBufferSize = 5000;

    // LLM
    private string _llmEndpoint = "http://127.0.0.1:1234";
    private string _llmApiKey = string.Empty;
    private string _llmAuthScheme = "Bearer";
    private string _llmModel = "codellama";
    private string _llmCompletionsPath = "v1/chat/completions";
    private int _llmMaxTokens = 4096;
    private double _llmTemperature = 0.2;
    private int _llmMaxIterations = 20;
    private int _llmHttpTimeoutSeconds = 120;
    private bool _llmEnableAutoComplete;
    private bool _llmEnableChat = true;
    private bool _llmStream = true;
    private string _llmActivePromptTemplate = "general";

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(ISettingsService settingsService, ILanguageServerManager serverManager)
    {
        _settingsService = settingsService;
        _serverManager = serverManager;
        _settings = settingsService.AppSettings;

        Sections =
        [
            "General",
            "Editor",
            "Terminal",
            "Language Servers",
            "LLM"
        ];
    }

    public ObservableCollection<string> Sections { get; }
    public ObservableCollection<LanguageServerItemViewModel> LanguageServers { get; } = [];

    // Section navigation
    public int SelectedSectionIndex
    {
        get => _selectedSectionIndex;
        set
        {
            if (SetProperty(ref _selectedSectionIndex, value))
            {
                OnPropertyChanged(nameof(IsGeneralVisible));
                OnPropertyChanged(nameof(IsEditorVisible));
                OnPropertyChanged(nameof(IsTerminalVisible));
                OnPropertyChanged(nameof(IsLanguageServersVisible));
                OnPropertyChanged(nameof(IsLlmVisible));
            }
        }
    }

    public bool IsGeneralVisible => _selectedSectionIndex == 0;
    public bool IsEditorVisible => _selectedSectionIndex == 1;
    public bool IsTerminalVisible => _selectedSectionIndex == 2;
    public bool IsLanguageServersVisible => _selectedSectionIndex == 3;
    public bool IsLlmVisible => _selectedSectionIndex == 4;

    // General properties
    public bool RestorePreviousSession
    {
        get => _restorePreviousSession;
        set => SetProperty(ref _restorePreviousSession, value);
    }

    public bool CheckUpdatesOnStartup
    {
        get => _checkUpdatesOnStartup;
        set => SetProperty(ref _checkUpdatesOnStartup, value);
    }

    // Editor properties
    public int FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, value);
    }

    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, value);
    }

    public int TabSize
    {
        get => _tabSize;
        set => SetProperty(ref _tabSize, value);
    }

    public bool InsertSpaces
    {
        get => _insertSpaces;
        set => SetProperty(ref _insertSpaces, value);
    }

    public bool WordWrap
    {
        get => _wordWrap;
        set => SetProperty(ref _wordWrap, value);
    }

    public bool LineNumbers
    {
        get => _lineNumbers;
        set => SetProperty(ref _lineNumbers, value);
    }

    public bool HighlightCurrentLine
    {
        get => _highlightCurrentLine;
        set => SetProperty(ref _highlightCurrentLine, value);
    }

    public bool ShowWhitespace
    {
        get => _showWhitespace;
        set => SetProperty(ref _showWhitespace, value);
    }

    public bool AutoSave
    {
        get => _autoSave;
        set => SetProperty(ref _autoSave, value);
    }

    public int AutoSaveDelay
    {
        get => _autoSaveDelay;
        set => SetProperty(ref _autoSaveDelay, value);
    }

    // Terminal properties
    public string TerminalFontFamily
    {
        get => _terminalFontFamily;
        set => SetProperty(ref _terminalFontFamily, value);
    }

    public int TerminalFontSize
    {
        get => _terminalFontSize;
        set => SetProperty(ref _terminalFontSize, value);
    }

    public int TerminalBufferSize
    {
        get => _terminalBufferSize;
        set => SetProperty(ref _terminalBufferSize, value);
    }

    // LLM properties
    public string LlmEndpoint
    {
        get => _llmEndpoint;
        set => SetProperty(ref _llmEndpoint, value);
    }

    public string LlmApiKey
    {
        get => _llmApiKey;
        set => SetProperty(ref _llmApiKey, value);
    }

    public string LlmAuthScheme
    {
        get => _llmAuthScheme;
        set => SetProperty(ref _llmAuthScheme, value);
    }

    public string LlmModel
    {
        get => _llmModel;
        set => SetProperty(ref _llmModel, value);
    }

    public string LlmCompletionsPath
    {
        get => _llmCompletionsPath;
        set => SetProperty(ref _llmCompletionsPath, value);
    }

    public int LlmMaxTokens
    {
        get => _llmMaxTokens;
        set => SetProperty(ref _llmMaxTokens, value);
    }

    public double LlmTemperature
    {
        get => _llmTemperature;
        set => SetProperty(ref _llmTemperature, value);
    }

    public int LlmMaxIterations
    {
        get => _llmMaxIterations;
        set => SetProperty(ref _llmMaxIterations, value);
    }

    public int LlmHttpTimeoutSeconds
    {
        get => _llmHttpTimeoutSeconds;
        set => SetProperty(ref _llmHttpTimeoutSeconds, value);
    }

    public bool LlmStream
    {
        get => _llmStream;
        set => SetProperty(ref _llmStream, value);
    }

    public bool LlmEnableAutoComplete
    {
        get => _llmEnableAutoComplete;
        set => SetProperty(ref _llmEnableAutoComplete, value);
    }

    public bool LlmEnableChat
    {
        get => _llmEnableChat;
        set => SetProperty(ref _llmEnableChat, value);
    }

    public string LlmActivePromptTemplate
    {
        get => _llmActivePromptTemplate;
        set => SetProperty(ref _llmActivePromptTemplate, value);
    }

    public async Task InitializeAsync()
    {
        LoadFromSettings(_settings);
        await LoadLanguageServersAsync();
    }

    private void LoadFromSettings(AppSettings settings)
    {
        // General
        RestorePreviousSession = settings.RestorePreviousSession;
        CheckUpdatesOnStartup = settings.CheckUpdatesOnStartup;

        // Editor
        FontSize = settings.Editor.FontSize;
        FontFamily = settings.Editor.FontFamily;
        TabSize = settings.Editor.TabSize;
        InsertSpaces = settings.Editor.InsertSpaces;
        WordWrap = settings.Editor.WordWrap;
        LineNumbers = settings.Editor.LineNumbers;
        HighlightCurrentLine = settings.Editor.HighlightCurrentLine;
        ShowWhitespace = settings.Editor.ShowWhitespace;
        AutoSave = settings.Editor.AutoSave;
        AutoSaveDelay = settings.Editor.AutoSaveDelay;

        // LLM
        LlmEndpoint = settings.Llm.Endpoint;
        LlmApiKey = settings.Llm.ApiKey;
        LlmAuthScheme = settings.Llm.AuthScheme;
        LlmModel = settings.Llm.Model;
        LlmCompletionsPath = settings.Llm.CompletionsPath;
        LlmMaxTokens = settings.Llm.MaxTokens;
        LlmTemperature = settings.Llm.Temperature;
        LlmMaxIterations = settings.Llm.MaxIterations;
        LlmHttpTimeoutSeconds = settings.Llm.HttpTimeoutSeconds;
        LlmStream = settings.Llm.Stream;
        LlmEnableAutoComplete = settings.Llm.EnableAutoComplete;
        LlmEnableChat = settings.Llm.EnableChat;
        LlmActivePromptTemplate = settings.Llm.ActivePromptTemplate;

        // Terminal
        TerminalFontFamily = settings.Terminal.FontFamily;
        TerminalFontSize = settings.Terminal.FontSize;
        TerminalBufferSize = settings.Terminal.BufferSize;
    }

    private async Task LoadLanguageServersAsync()
    {
        var servers = _serverManager.GetAvailableServers();
        LanguageServers.Clear();

        foreach (var server in servers)
        {
            var status = await _serverManager.CheckServerStatusAsync(server.Id);
            var userConfig = _settings.LanguageServers.GetValueOrDefault(server.Id);

            var item = new LanguageServerItemViewModel(server)
            {
                Status = status,
                IsEnabled = userConfig?.Enabled ?? true,
                CustomCommand = userConfig?.CustomCommand,
                CustomArgs = userConfig?.CustomArgs is { Count: > 0 }
                    ? string.Join(" ", userConfig.CustomArgs)
                    : null,
            };

            LanguageServers.Add(item);
        }
    }

    [RelayCommand]
    private async Task InstallServer(LanguageServerItemViewModel? item)
    {
        if (item is null || item.IsInstalling)
            return;

        item.IsInstalling = true;
        item.StatusText = "Installing...";

        var progress = new Progress<string>(msg => item.StatusText = msg);
        var success = await _serverManager.InstallServerAsync(item.Definition.Id, progress);

        item.IsInstalling = false;
        item.Status = success
            ? LanguageServerStatus.Installed
            : item.Status;
        item.StatusText = success ? "Installed" : item.StatusText;
    }

    [RelayCommand]
    private async Task Save()
    {
        var newSettings = _settings with
        {
            RestorePreviousSession = RestorePreviousSession,
            CheckUpdatesOnStartup = CheckUpdatesOnStartup,
            Editor = new EditorSettings
            {
                FontSize = FontSize,
                FontFamily = FontFamily,
                TabSize = TabSize,
                InsertSpaces = InsertSpaces,
                WordWrap = WordWrap,
                LineNumbers = LineNumbers,
                HighlightCurrentLine = HighlightCurrentLine,
                ShowWhitespace = ShowWhitespace,
                AutoSave = AutoSave,
                AutoSaveDelay = AutoSaveDelay,
            },
            Llm = new LlmSettings
            {
                Endpoint = LlmEndpoint,
                ApiKey = LlmApiKey,
                AuthScheme = LlmAuthScheme,
                Model = LlmModel,
                CompletionsPath = LlmCompletionsPath,
                MaxTokens = LlmMaxTokens,
                Temperature = LlmTemperature,
                MaxIterations = LlmMaxIterations,
                HttpTimeoutSeconds = LlmHttpTimeoutSeconds,
                Stream = LlmStream,
                EnableAutoComplete = LlmEnableAutoComplete,
                EnableChat = LlmEnableChat,
                ActivePromptTemplate = LlmActivePromptTemplate,
            },
            Terminal = new TerminalSettings
            {
                FontFamily = TerminalFontFamily,
                FontSize = TerminalFontSize,
                BufferSize = TerminalBufferSize,
            },
            LanguageServers = BuildLanguageServerConfigs(),
        };

        await _settingsService.SaveAppSettingsAsync(newSettings);
        IsSaved = true;
    }

    private Dictionary<string, LanguageServerUserConfig> BuildLanguageServerConfigs()
    {
        var configs = new Dictionary<string, LanguageServerUserConfig>();
        foreach (var item in LanguageServers)
        {
            configs[item.Definition.Id] = new LanguageServerUserConfig
            {
                Enabled = item.IsEnabled,
                CustomCommand = string.IsNullOrWhiteSpace(item.CustomCommand) ? null : item.CustomCommand,
                CustomArgs = string.IsNullOrWhiteSpace(item.CustomArgs)
                    ? []
                    : item.CustomArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            };
        }
        return configs;
    }

    public bool IsSaved { get; private set; }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class LanguageServerItemViewModel : INotifyPropertyChanged
{
    private LanguageServerStatus _status;
    private bool _isEnabled;
    private bool _isInstalling;
    private string _statusText = "";
    private string? _customCommand;
    private string? _customArgs;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LanguageServerItemViewModel(LanguageServerDefinition definition)
    {
        Definition = definition;
        LanguageDisplay = string.Join(", ", definition.Languages);
        InstallMethodDisplay = definition.InstallMethod switch
        {
            InstallMethod.Npm => "npm",
            InstallMethod.Pip => "pip",
            InstallMethod.DotnetTool => "dotnet tool",
            InstallMethod.Cargo => "cargo",
            InstallMethod.GoInstall => "go install",
            InstallMethod.BinaryDownload => "Manual download",
            _ => "Unknown",
        };
    }

    public LanguageServerDefinition Definition { get; }
    public string LanguageDisplay { get; }
    public string InstallMethodDisplay { get; }

    public LanguageServerStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusIcon));
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        set
        {
            if (_isInstalling != value)
            {
                _isInstalling = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public string? CustomCommand
    {
        get => _customCommand;
        set
        {
            if (_customCommand != value)
            {
                _customCommand = value;
                OnPropertyChanged();
            }
        }
    }

    public string? CustomArgs
    {
        get => _customArgs;
        set
        {
            if (_customArgs != value)
            {
                _customArgs = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusIcon => _status switch
    {
        LanguageServerStatus.Installed => "✓",
        LanguageServerStatus.NotInstalled => "✗",
        _ => "?",
    };

    public bool CanInstall => _status == LanguageServerStatus.NotInstalled && !_isInstalling;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
