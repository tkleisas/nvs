using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.LLM;
using NVS.Services.LLM;

namespace NVS.ViewModels;

/// <summary>
/// Backs the Ctrl+I inline AI edit popup: an instruction over the current selection
/// (or the caret position), an LLM-generated proposal, a diff preview, and apply/cancel.
/// </summary>
public partial class InlineChatViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string _contextText = string.Empty;
    private bool _hadSelection;
    private string _proposed = string.Empty;

    [ObservableProperty]
    private string _instruction = string.Empty;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasPreview;

    [ObservableProperty]
    private string _contextSummary = string.Empty;

    /// <summary>Proposal text streamed in live while the LLM is generating.</summary>
    [ObservableProperty]
    private string _liveProposal = string.Empty;

    public ObservableCollection<DiffRow> PreviewRows { get; } = [];

    /// <summary>Optional LLM factory for tests; defaults to the app's ILlmService from DI.</summary>
    public Func<ILlmService?>? LlmServiceProvider { get; set; }

    public InlineChatViewModel(MainViewModel main)
    {
        _main = main;
    }

    /// <summary>Opens the popup over the active document's selection (or caret).</summary>
    public void Open()
    {
        var doc = _main.Editor?.ActiveDocument;
        if (doc?.Selection is null)
        {
            _main.StatusMessage = "Open a document first";
            return;
        }

        _hadSelection = doc.Selection.HasSelection;
        _contextText = _hadSelection ? doc.Selection.SelectedText : string.Empty;
        ContextSummary = _hadSelection
            ? $"Editing selection ({_contextText.Split('\n').Length} line(s))"
            : "Generating code at caret";
        Instruction = string.Empty;
        HasPreview = false;
        PreviewRows.Clear();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private async Task Submit()
    {
        if (string.IsNullOrWhiteSpace(Instruction)) return;

        var llm = LlmServiceProvider?.Invoke()
            ?? App.Current?.Services?.GetService(typeof(ILlmService)) as ILlmService;
        if (llm is null || !llm.IsConfigured)
        {
            _main.StatusMessage = "LLM is not configured — set it up in Settings → LLM";
            return;
        }

        var doc = _main.Editor?.ActiveDocument;
        if (doc?.Selection is null)
        {
            Close();
            return;
        }

        var language = doc.Document.Language.ToString();
        var (system, user) = InlineEditPrompts.Build(Instruction, _contextText, language, _hadSelection);

        IsBusy = true;
        LiveProposal = string.Empty;
        try
        {
            var sb = new System.Text.StringBuilder();
            var response = await llm.SendAsync(
                new ChatCompletionRequest
                {
                    Model = string.Empty,
                    Messages =
                    [
                        ChatCompletionMessage.System(system),
                        ChatCompletionMessage.User(user)
                    ],
                    Stream = true,
                },
                onToken: token =>
                {
                    sb.Append(token);
                    LiveProposal = sb.ToString();
                });

            _proposed = InlineEditPrompts.ExtractCode(response.Content);
            if (string.IsNullOrWhiteSpace(_proposed))
            {
                _main.StatusMessage = "AI returned an empty proposal";
                return;
            }

            PreviewRows.Clear();
            foreach (var row in SimpleDiffer.Diff(_contextText, _proposed))
            {
                PreviewRows.Add(row);
            }

            HasPreview = true;
        }
        catch (Exception ex)
        {
            _main.StatusMessage = $"AI edit failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            LiveProposal = string.Empty;
        }
    }

    [RelayCommand]
    private void Apply()
    {
        if (!HasPreview) return;

        var doc = _main.Editor?.ActiveDocument;
        if (doc?.Selection is null)
        {
            Close();
            return;
        }

        doc.Selection.ReplaceSelectionOrInsertAtCaret(_proposed);
        _main.StatusMessage = "AI edit applied";
        Close();
    }
}
