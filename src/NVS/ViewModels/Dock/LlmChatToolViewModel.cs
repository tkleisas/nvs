using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

        _conversationHistory.Add(ChatCompletionMessage.User(input));

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
        StatusText = "Ready";
    }

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
        return new PromptContext
        {
            WorkspacePath = Main.WorkspacePath,
            ActiveFilePath = activeDoc?.Document.FilePath,
            ActiveFileLanguage = activeDoc?.Language.ToString(),
            SelectedText = null
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
