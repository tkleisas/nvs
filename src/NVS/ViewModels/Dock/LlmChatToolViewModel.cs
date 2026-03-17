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
        Title = "🤖 Chat";
        CanClose = true;
        CanPin = true;
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
                maxIterations: Main.SettingsService.AppSettings.Llm.MaxIterations,
                cancellationToken: _streamCts.Token);

            if (string.IsNullOrEmpty(assistantBubble.Content))
                assistantBubble.SetContent(result.Content);

            _conversationHistory.Add(ChatCompletionMessage.Assistant(result.Content));

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
        catch { /* file picker cancelled or error */ }
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
        catch { /* file picker cancelled or error */ }
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
        string? gitBranch = Main.CurrentBranch is { Length: > 0 } b ? b : null;
        string? gitStatusSummary = null;
        if (Main.GitChangedFiles.Count > 0 || Main.GitStagedFiles.Count > 0)
        {
            gitStatusSummary = $"{Main.GitChangedFiles.Count} changed, {Main.GitStagedFiles.Count} staged";
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
                catch { /* skip unreadable files */ }
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
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ChatBubble(string role, string content)
    {
        Role = role;
        _content = content;
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
