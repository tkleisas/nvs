using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;
using NVS.Core.LLM;
using NVS.Core.Models;
using NVS.Services.LLM;

namespace NVS.ViewModels.Dock;

public sealed partial class LlmChatToolViewModel : Tool
{
    private string _userInput = string.Empty;
    private string _selectedTaskMode = "general";
    private string? _selectedModelName;
    private bool _isSending;
    private bool _isStreaming;
    private string _statusText = "Ready";
    private ChatSession? _currentSession;

    public MainViewModel Main { get; }

    /// <summary>Files attached to the next message for context.</summary>
    public ObservableCollection<string> AttachedFiles { get; } = [];

    /// <summary>Images attached to the next message (base64 data URIs).</summary>
    public ObservableCollection<string> AttachedImages { get; } = [];

    public ObservableCollection<ChatBubble> Messages { get; } = [];
    public ObservableCollection<ChatSession> Sessions { get; } = [];

    public ChatSession? CurrentSession
    {
        get => _currentSession;
        set
        {
            if (_currentSession != value)
            {
                _currentSession = value;
                OnPropertyChanged();
                // Sync the model dropdown to the session's persisted model
                _selectedModelName = value?.ModelId;
                if (!string.IsNullOrEmpty(_selectedModelName) && !AvailableModelNames.Contains(_selectedModelName))
                    RefreshModelList();
                OnPropertyChanged(nameof(SelectedModelName));
                _ = LoadCurrentSessionMessagesAsync();
            }
        }
    }

    public string UserInput
    {
        get => _userInput;
        set { if (_userInput != value) { _userInput = value; OnPropertyChanged(); SendMessageCommand.NotifyCanExecuteChanged(); } }
    }

    public string SelectedTaskMode
    {
        get => _selectedTaskMode;
        set
        {
            if (_selectedTaskMode != value)
            {
                _selectedTaskMode = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Display names of all enabled models from settings, for the toolbar ComboBox.</summary>
    public ObservableCollection<string> AvailableModelNames { get; } = ["(default)"];

    /// <summary>The model name selected in the toolbar. Null or empty means "use global default".
    /// Changing this persists the choice on the current chat session.</summary>
    public string? SelectedModelName
    {
        get => _selectedModelName;
        set
        {
            if (_selectedModelName == value) return;
            _selectedModelName = value;
            OnPropertyChanged();
            _ = OnModelChangedAsync();
        }
    }

    public bool IsSending
    {
        get => _isSending;
        private set { if (_isSending != value) { _isSending = value; OnPropertyChanged(); SendMessageCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged(); } }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        private set { if (_isStreaming != value) { _isStreaming = value; OnPropertyChanged(); } }
    }

    public string StatusText
    {
        get => _statusText;
        private set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    public static IReadOnlyList<string> TaskModes { get; } = ["general", "coding", "debugging", "testing"];

    private readonly List<ChatCompletionMessage> _conversationHistory = [];
    private CancellationTokenSource? _streamCts;

    public LlmChatToolViewModel(MainViewModel main)
{
        Main = main;
        Id = "LlmChat";
        Title = "?? Chat";
        CanClose = true;
        CanPin = true;
        RefreshModelList();
    }

    /// <summary>Load sessions from the workspace database. Called when workspace opens.</summary>
    public async Task LoadSessionsAsync()
    {
        var sessionService = Main.ChatSessionService;
        if (sessionService is null || !sessionService.IsOpen) return;

        try
        {
            var sessions = await sessionService.GetSessionsAsync();
            Sessions.Clear();
            foreach (var s in sessions)
                Sessions.Add(s);

            // Select the most recent session, or create one if none exist
            if (Sessions.Count > 0)
            {
                CurrentSession = Sessions[0];
            }
            else
            {
                await CreateNewSessionAsync("New Chat");
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load chat sessions");
        }
    }

    [RelayCommand]
    private async Task NewSession()
    {
        await CreateNewSessionAsync("New Chat");
    }

    [RelayCommand]
    private async Task DeleteSession()
    {
        var sessionService = Main.ChatSessionService;
        if (sessionService is null || !sessionService.IsOpen || _currentSession is null) return;

        try
        {
            var idToDelete = _currentSession.Id;
            await sessionService.DeleteSessionAsync(idToDelete);

            var toRemove = Sessions.FirstOrDefault(s => s.Id == idToDelete);
            if (toRemove is not null)
                Sessions.Remove(toRemove);

            if (Sessions.Count > 0)
            {
                CurrentSession = Sessions[0];
            }
            else
            {
                await CreateNewSessionAsync("New Chat");
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to delete chat session");
        }
    }

    private async Task CreateNewSessionAsync(string title)
    {
        var sessionService = Main.ChatSessionService;
        if (sessionService is null || !sessionService.IsOpen) return;

        var session = await sessionService.CreateSessionAsync(title, SelectedTaskMode);
        Sessions.Insert(0, session);
        CurrentSession = session;
        // Persist the current model selection immediately on the new session
        _ = OnModelChangedAsync();
    }

    private async Task LoadCurrentSessionMessagesAsync()
    {
        Messages.Clear();
        _conversationHistory.Clear();

        var sessionService = Main.ChatSessionService;
        if (sessionService is null || !sessionService.IsOpen || _currentSession is null) return;

        try
        {
            var records = await sessionService.GetMessagesAsync(_currentSession.Id);
            foreach (var record in records)
            {
                Messages.Add(new ChatBubble(record.Role, record.Content));

                if (record.Role is "user" or "assistant" or "system")
                {
                    _conversationHistory.Add(record.Role switch
                    {
                        "user" => ChatCompletionMessage.User(record.Content),
                        "assistant" => ChatCompletionMessage.Assistant(record.Content),
                        _ => ChatCompletionMessage.System(record.Content)
                    });
                }
            }

            SelectedTaskMode = _currentSession.TaskMode;
            StatusText = $"Session loaded · {records.Count} messages";
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load messages for session {Id}", _currentSession.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessage()
    {
        var input = UserInput.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;

        var llmService = GetLlmService();
        if (llmService is null)
        {
            Messages.Add(new ChatBubble("system", "LLM service is not configured. Check Settings → LLM."));
            return;
        }

        // Ensure we have a session
        if (_currentSession is null)
        {
            await CreateNewSessionAsync(GenerateTitle(input));
        }

        Messages.Add(new ChatBubble("user", input));
        UserInput = string.Empty;

        // Build message with optional images
        var userMessage = AttachedImages.Count > 0
            ? ChatCompletionMessage.UserWithImages(input, AttachedImages)
            : ChatCompletionMessage.User(input);
        _conversationHistory.Add(userMessage);

        // Persist user message and auto-title
        await PersistMessageAsync("user", input);
        await AutoTitleSessionAsync(input);

        var assistantBubble = new ChatBubble("assistant", string.Empty);
        Messages.Add(assistantBubble);

        IsSending = true;
        IsStreaming = true;
        StatusText = "Thinking...";
        _streamCts = new CancellationTokenSource();

        try
        {
            var systemPrompt = PromptLoader.BuildPrompt(
                SelectedTaskMode,
                BuildPromptContext());

            var tools = (llmService as LlmService)?.GetToolDefinitions();

            var result = await llmService.RunAgentLoopAsync(
                new List<ChatCompletionMessage>(_conversationHistory),
                tools,
                systemPrompt,
                onToken: token =>
                {
                    assistantBubble.AppendContent(token);
                    StatusText = "Streaming...";
                },
                onToolCall: toolCall =>
                {
                    StatusText = $"Running {toolCall.ToolName}...";
                    Messages.Add(new ChatBubble("tool", $"⚡ {toolCall.ToolName}: {(toolCall.Success ? "✓" : "✗")} ({toolCall.Duration.TotalMilliseconds:F0}ms)"));
                },
                onApprovalRequired: RequestToolApprovalAsync,
                maxIterations: Main.SettingsService.AppSettings.Llm.MaxIterations,
                cancellationToken: _streamCts.Token,
                modelId: ResolveModelId(_selectedModelName));

            if (string.IsNullOrEmpty(assistantBubble.Content))
                assistantBubble.SetContent(result.Content);

            _conversationHistory.Add(new ChatCompletionMessage
            {
                Role = "assistant",
                Content = result.Content,
                ReasoningContent = result.ReasoningContent
            });

            // Persist assistant response
            await PersistMessageAsync("assistant", result.Content);

            StatusText = $"Done · {result.TotalInputTokens + result.TotalOutputTokens} tokens · {result.Iterations} iteration(s)";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled";
            if (string.IsNullOrEmpty(assistantBubble.Content))
                assistantBubble.SetContent("(cancelled)");
        }
        catch (Exception ex)
        {
            StatusText = "Error";
            assistantBubble.SetContent($"Error: {ex.Message}");
        }
        finally
        {
            IsSending = false;
            IsStreaming = false;
            AttachedFiles.Clear();
            AttachedImages.Clear();
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(UserInput);

    /// <summary>
    /// Shows an inline Allow/Deny prompt for a destructive tool call and waits for
    /// the user's decision. Stopping the generation counts as a denial.
    /// </summary>
    private async Task<bool> RequestToolApprovalAsync(ToolApprovalRequest request)
    {
        var bubble = new ChatBubble("approval", FormatApprovalPrompt(request));
        bubble.BeginApproval();
        Messages.Add(bubble);
        StatusText = $"Waiting for approval: {request.ToolName}";

        using var cancelRegistration = _streamCts is not null
            ? _streamCts.Token.Register(() => bubble.ResolveApproval(false))
            : default;

        return await bubble.ApprovalTask;
    }

    private static string FormatApprovalPrompt(ToolApprovalRequest request)
    {
        const int maxArgumentChars = 600;

        var arguments = request.Arguments;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(arguments);
            arguments = System.Text.Json.JsonSerializer.Serialize(
                doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch (System.Text.Json.JsonException)
        {
            // Show the raw arguments if they aren't valid JSON.
        }

        if (arguments.Length > maxArgumentChars)
            arguments = arguments[..maxArgumentChars] + "\n… (truncated)";

        return $"🛡 The assistant wants to run \"{request.ToolName}\":\n{arguments}";
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _streamCts?.Cancel();
    }

    private bool CanStop() => IsSending;

    [RelayCommand]
    private void ClearChat()
    {
        Messages.Clear();
        _conversationHistory.Clear();
        AttachedFiles.Clear();
        AttachedImages.Clear();
        StatusText = "Ready";
    }

    [RelayCommand]
    private async Task AttachFile()
    {
        try
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

            if (topLevel is null) return;

            var storage = topLevel.StorageProvider;
            var files = await storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Attach File to Chat",
                AllowMultiple = true
            });

            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (path is not null && !AttachedFiles.Contains(path))
                    AttachedFiles.Add(path);
            }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "File attach failed or was cancelled"); }
    }

    [RelayCommand]
    private void RemoveAttachedFile(string? filePath)
    {
        if (filePath is not null)
            AttachedFiles.Remove(filePath);
    }

    [RelayCommand]
    private async Task AttachImage()
    {
        try
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

            if (topLevel is null) return;

            var storage = topLevel.StorageProvider;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Attach Image",
                AllowMultiple = true,
                FileTypeFilter = [new("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"] }]
            });

            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (path is null || !File.Exists(path)) continue;

                var bytes = await File.ReadAllBytesAsync(path);
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var mime = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".bmp" => "image/bmp",
                    ".webp" => "image/webp",
                    _ => "image/png"
                };
                var dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                AttachedImages.Add(dataUri);
            }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "File attach failed or was cancelled"); }
    }

    [RelayCommand]
    private void RemoveAttachedImage(int index)
    {
        if (index >= 0 && index < AttachedImages.Count)
            AttachedImages.RemoveAt(index);
    }

    /// <summary>Whether the current LLM model supports vision (images in messages).</summary>
    public bool SupportsVision => Main.SettingsService.AppSettings.Llm.SupportsVision;

    private async Task PersistMessageAsync(string role, string content)
    {
        var sessionService = Main.ChatSessionService;
        if (sessionService is null || !sessionService.IsOpen || _currentSession is null) return;

        try
        {
            await sessionService.SaveMessageAsync(_currentSession.Id, role, content);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to persist {Role} message", role);
        }
    }

    private async Task AutoTitleSessionAsync(string firstInput)
    {
        var sessionService = Main.ChatSessionService;
        if (sessionService is null || !sessionService.IsOpen || _currentSession is null) return;

        // Only auto-title if the session is still "New Chat"
        if (_currentSession.Title != "New Chat") return;

        var title = GenerateTitle(firstInput);

        try
        {
            await sessionService.UpdateSessionTitleAsync(_currentSession.Id, title);

            // Update local session reference
            var idx = Sessions.IndexOf(_currentSession);
            var updated = _currentSession with { Title = title };
            _currentSession = updated;
            if (idx >= 0)
                Sessions[idx] = updated;

            OnPropertyChanged(nameof(CurrentSession));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to auto-title session");
        }
    }

    private static string GenerateTitle(string input)
    {
        var title = input.ReplaceLineEndings(" ").Trim();
        return title.Length > 60 ? string.Concat(title.AsSpan(0, 57), "...") : title;
    }

    private void RefreshModelList()
    {
        var service = GetLlmServiceInternal();
        var models = service?.GetAvailableModels();
        var settings = Main.SettingsService.AppSettings.Llm;

        AvailableModelNames.Clear();
        AvailableModelNames.Add("(default)");
        if (models is not null)
        {
            foreach (var m in models)
                if (!string.IsNullOrWhiteSpace(m.DisplayName) && m.Enabled)
                    AvailableModelNames.Add(m.DisplayName);
        }
    }

    private async Task OnModelChangedAsync()
    {
        if (_currentSession is null) return;
        var service = Main.ChatSessionService;
        if (service is null || !service.IsOpen) return;

        var settings = Main.SettingsService.AppSettings.Llm;
        var modelId = ResolveModelId(_selectedModelName);
        await service.UpdateSessionModelAsync(_currentSession.Id, modelId);
    }

    /// <summary>Maps a DisplayName back to a ModelId using the configured models list.</summary>
    internal string? ResolveModelId(string? displayNameOrDefault)
    {
        if (string.IsNullOrWhiteSpace(displayNameOrDefault) || displayNameOrDefault == "(default)")
            return null;
        var service = GetLlmServiceInternal();
        var models = service?.GetAvailableModels();
        return models?.FirstOrDefault(m => m.DisplayName == displayNameOrDefault)?.ModelId;
    }

    private ILlmService? GetLlmServiceInternal()
    {
        try
        {
            return App.Current?.Services?.GetService(typeof(ILlmService)) as ILlmService;
        }
        catch { return null; }
    }

    private ILlmService? GetLlmService()
    {
        try
        {
            var service = App.Current?.Services?.GetService(typeof(ILlmService)) as ILlmService;
            return service?.IsConfigured == true ? service : null;
        }
        catch
        {
            return null;
        }
    }

    private PromptContext BuildPromptContext()
    {
        var activeDoc = Main.Editor?.ActiveDocument;

        // Collect open file paths
        var openFiles = Main.Editor?.OpenDocuments
            .Select(d => d.Document.FilePath ?? d.Title)
            .ToList();

        // Collect diagnostics from all open documents (errors + warnings, max 20)
        var diagnostics = Main.Editor?.OpenDocuments
            .SelectMany(d => d.Diagnostics ?? [])
            .Select(d => $"[{d.Severity}] {d.Source}: {d.Message} ({d.Range.Start.Line}:{d.Range.Start.Column})")
            .Take(20)
            .ToList();

        // Git context
        string? gitBranch = Main.Git.CurrentBranch is { Length: > 0 } b ? b : null;
        string? gitStatusSummary = null;
        if (Main.Git.ChangedFiles.Count > 0 || Main.Git.StagedFiles.Count > 0)
        {
            gitStatusSummary = $"{Main.Git.ChangedFiles.Count} changed, {Main.Git.StagedFiles.Count} staged";
        }

        // Attached files: read contents
        List<string>? attachedContents = null;
        if (AttachedFiles.Count > 0)
        {
            attachedContents = [];
            foreach (var filePath in AttachedFiles)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var content = File.ReadAllText(filePath);
                        // Cap at 10KB per file
                        if (content.Length > 10240)
                            content = content[..10240] + "\n... (truncated)";
                        attachedContents.Add($"### {Path.GetFileName(filePath)}\n```\n{content}\n```");
                    }
                }
                catch (Exception ex) { Serilog.Log.Debug(ex, "Skipping unreadable attached file"); }
            }
        }

        // Solution name from workspace path
        var solutionName = Main.ProjectNames.Count > 0
            ? string.Join(", ", Main.ProjectNames.Take(3))
            : (Main.WorkspacePath is not null ? Path.GetFileName(Main.WorkspacePath) : null);

        return new PromptContext
        {
            WorkspacePath = Main.WorkspacePath,
            ActiveFilePath = activeDoc?.Document.FilePath,
            ActiveFileLanguage = activeDoc?.Language.ToString(),
            SolutionName = solutionName,
            SelectedText = null,
            OpenFiles = openFiles,
            Diagnostics = diagnostics is { Count: > 0 } ? diagnostics : null,
            GitBranch = gitBranch,
            GitStatusSummary = gitStatusSummary,
            AttachedFiles = attachedContents
        };
    }

    private new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
    }
}

/// <summary>A single message bubble in the chat UI.</summary>
public sealed class ChatBubble : INotifyPropertyChanged
{
    private string _content;
    private bool _isApprovalPending;
    private TaskCompletionSource<bool>? _approvalTcs;

    public string Role { get; }
    public string Content
    {
        get => _content;
        private set
        {
            if (_content != value)
            {
                _content = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
            }
        }
    }

    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsSystem => Role == "system";
    public bool IsTool => Role == "tool";
    public bool IsApproval => Role == "approval";
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;

    /// <summary>Whether this approval bubble is still waiting for the user's decision.</summary>
    public bool IsApprovalPending
    {
        get => _isApprovalPending;
        private set
        {
            if (_isApprovalPending != value)
            {
                _isApprovalPending = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsApprovalPending)));
            }
        }
    }

    public IRelayCommand AllowCommand { get; }
    public IRelayCommand DenyCommand { get; }

    /// <summary>Completes when the user allows or denies this approval bubble.</summary>
    public Task<bool> ApprovalTask => _approvalTcs?.Task ?? Task.FromResult(false);

    public event PropertyChangedEventHandler? PropertyChanged;

    public ChatBubble(string role, string content)
    {
        Role = role;
        _content = content;
        AllowCommand = new RelayCommand(() => ResolveApproval(true));
        DenyCommand = new RelayCommand(() => ResolveApproval(false));
    }

    /// <summary>Put the bubble into the waiting-for-decision state.</summary>
    public void BeginApproval()
    {
        _approvalTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsApprovalPending = true;
    }

    /// <summary>Record the user's decision; safe to call more than once.</summary>
    public void ResolveApproval(bool approved)
    {
        if (!IsApprovalPending) return;
        IsApprovalPending = false;
        Content += approved ? "\n✓ Allowed" : "\n✗ Denied";
        _approvalTcs?.TrySetResult(approved);
    }

    /// <summary>Append a token during streaming.</summary>
    public void AppendContent(string token)
    {
        Content += token;
    }

    /// <summary>Replace content entirely.</summary>
    public void SetContent(string content)
    {
        Content = content;
    }
}
