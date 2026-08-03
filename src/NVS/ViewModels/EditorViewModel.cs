using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using Range = NVS.Core.Models.Range;

namespace NVS.ViewModels;

public partial class EditorViewModel : INotifyPropertyChanged
{
    private readonly IEditorService _editorService;
    private readonly IFileSystemService _fileSystemService;
    private readonly ILspSessionManager? _lspSessionManager;

    /// <summary>The LSP session manager (used by panels like the document outline).</summary>
    public ILspSessionManager? LspSessionManager => _lspSessionManager;
    private readonly IBreakpointStore? _breakpointStore;
    private readonly ICodeMetricsService? _codeMetricsService;
    private CancellationTokenSource? _didChangeCts;

    private DocumentViewModel? _activeDocument;
    private int _activeTabIndex = -1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DocumentViewModel? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (_activeDocument != null)
                _activeDocument.PropertyChanged -= OnActiveDocumentPropertyChanged;

            _activeDocument = value;

            if (_activeDocument != null)
                _activeDocument.PropertyChanged += OnActiveDocumentPropertyChanged;

            if (value is not null)
            {
                _mruOrder.Remove(value);
                _mruOrder.Insert(0, value);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(CursorLine));
            OnPropertyChanged(nameof(CursorColumn));
        }
    }

    public int CursorLine => _activeDocument?.CursorLine ?? 1;
    public int CursorColumn => _activeDocument?.CursorColumn ?? 1;
    public string CurrentMethodInfo => GetCurrentMethodInfo();

    public int TotalErrors => OpenDocuments.Sum(d => d.ErrorCount);
    public int TotalWarnings => OpenDocuments.Sum(d => d.WarningCount);

    public string DiagnosticSummary
    {
        get
        {
            var errors = TotalErrors;
            var warnings = TotalWarnings;
            return $"{errors} error{(errors != 1 ? "s" : "")}, {warnings} warning{(warnings != 1 ? "s" : "")}";
        }
    }

    public int ActiveTabIndex
    {
        get => _activeTabIndex;
        set
        {
            _activeTabIndex = value;
            OnPropertyChanged();
        }
    }

    public bool HasNoOpenDocuments => OpenDocuments.Count == 0;

    // Split editor state
    private bool _isSplitActive;
    private int _splitTabIndex = -1;
    private bool _isSplitVertical = true;

    public bool IsSplitActive
    {
        get => _isSplitActive;
        set
        {
            if (_isSplitActive != value)
            {
                _isSplitActive = value;
                OnPropertyChanged();
            }
        }
    }

    public int SplitTabIndex
    {
        get => _splitTabIndex;
        set
        {
            _splitTabIndex = value;
            OnPropertyChanged();
        }
    }

    public bool IsSplitVertical
    {
        get => _isSplitVertical;
        set
        {
            if (_isSplitVertical != value)
            {
                _isSplitVertical = value;
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    private void SplitRight()
    {
        if (OpenDocuments.Count < 1) return;
        IsSplitVertical = true;
        IsSplitActive = true;
        SplitTabIndex = ActiveTabIndex;
    }

    [RelayCommand]
    private void SplitDown()
    {
        if (OpenDocuments.Count < 1) return;
        IsSplitVertical = false;
        IsSplitActive = true;
        SplitTabIndex = ActiveTabIndex;
    }

    [RelayCommand]
    private void CloseSplit()
    {
        IsSplitActive = false;
        SplitTabIndex = -1;
    }

    public ObservableCollection<DocumentViewModel> OpenDocuments { get; } = [];

    // Most-recently-used order, front = most recent (drives Ctrl+Tab).
    private readonly List<DocumentViewModel> _mruOrder = new();

    /// <summary>Activates the given document (used by the tab overflow list).</summary>
    [RelayCommand]
    private void ActivateDocument(DocumentViewModel? document)
    {
        if (document is not null)
        {
            ActiveDocument = document;
        }
    }

    /// <summary>Ctrl+Tab: jump to the most recently used document (repeated presses alternate).</summary>
    [RelayCommand]
    private void CycleToPreviousTab()
    {
        _mruOrder.RemoveAll(d => !OpenDocuments.Contains(d));
        if (_mruOrder.Count < 2) return;

        ActiveDocument = _mruOrder[1];
    }

    public EditorViewModel(IEditorService editorService, IFileSystemService fileSystemService, ILspSessionManager? lspSessionManager = null, IBreakpointStore? breakpointStore = null, ICodeMetricsService? codeMetricsService = null)
    {
        _editorService = editorService;
        _fileSystemService = fileSystemService;
        _lspSessionManager = lspSessionManager;
        _breakpointStore = breakpointStore;
        _codeMetricsService = codeMetricsService;

        _editorService.DocumentOpened += OnDocumentOpened;
        _editorService.DocumentClosed += OnDocumentClosed;
        _editorService.ActiveDocumentChanged += OnActiveDocumentChanged;

        if (_lspSessionManager is not null)
            _lspSessionManager.DiagnosticsChanged += OnLspDiagnosticsChanged;
    }

    [RelayCommand]
    public void NewFile()
    {
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Path = $"Untitled-{OpenDocuments.Count + 1}",
            Name = $"Untitled-{OpenDocuments.Count + 1}",
            State = DocumentState.Loaded,
            Language = Language.Unknown
        };

        var docVm = new DocumentViewModel(document);
        OpenDocuments.Add(docVm);
        ActiveDocument = docVm;
        ActiveTabIndex = OpenDocuments.Count - 1;
        OnPropertyChanged(nameof(HasNoOpenDocuments));
    }

    [RelayCommand]
    public async Task SaveFile()
    {
        if (ActiveDocument?.Document == null) return;

        if (string.IsNullOrEmpty(ActiveDocument.Document.FilePath))
        {
            return;
        }

        ActiveDocument.Document.Content = ActiveDocument.Text;
        await _editorService.SaveDocumentAsync(ActiveDocument.Document);
        ActiveDocument.IsDirty = false;
    }

    [RelayCommand]
    public async Task SaveFileAs()
    {
        if (ActiveDocument?.Document == null) return;
        await SaveFile();
    }

    [RelayCommand]
    public async Task SaveAll()
    {
        foreach (var docVm in OpenDocuments.Where(d => d.IsDirty))
        {
            await SaveDocumentAsync(docVm);
        }
    }

    [RelayCommand]
    public void CloseFile()
    {
        if (ActiveDocument == null) return;
        _ = CloseTabAsync(ActiveDocument);
    }

    [RelayCommand]
    public void CloseAllFiles()
    {
        foreach (var doc in OpenDocuments.ToList())
        {
            CloseDocument(doc);
        }
    }

    [RelayCommand]
    public void Undo() => ActiveDocument?.UndoCommand?.Execute(null);

    [RelayCommand]
    public void Redo() => ActiveDocument?.RedoCommand?.Execute(null);

    [RelayCommand]
    public void Find() => ActiveDocument?.OpenSearchCommand?.Execute(null);

    [RelayCommand]
    public void Replace() => ActiveDocument?.OpenReplaceCommand?.Execute(null);

    /// <summary>
    /// Updates diagnostics for the document matching the given URI.
    /// Called by the LspSessionManager diagnostics relay.
    /// </summary>
    public void UpdateDiagnostics(string documentUri, IReadOnlyList<Diagnostic> diagnostics)
    {
        var filePath = LspUriToFilePath(documentUri);
        var docVm = OpenDocuments.FirstOrDefault(d =>
            string.Equals(d.Document.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (docVm is not null)
        {
            docVm.Diagnostics = diagnostics;
            NotifyDiagnosticCountsChanged();
        }

        DiagnosticsReceived?.Invoke(filePath, diagnostics);
    }

    /// <summary>
    /// Optional sink notified whenever LSP diagnostics change for a file
    /// (used to feed the Problems panel, including files that are not open).
    /// </summary>
    public Action<string, IReadOnlyList<Diagnostic>>? DiagnosticsReceived { get; set; }

    private static string LspUriToFilePath(string uri)
    {
        try
        {
            return new Uri(uri).LocalPath;
        }
        catch
        {
            return uri;
        }
    }

    private async Task SaveDocumentAsync(DocumentViewModel docVm)
    {
        if (string.IsNullOrEmpty(docVm.Document.FilePath))
        {
            return;
        }

        docVm.Document.Content = docVm.Text;
        await _editorService.SaveDocumentAsync(docVm.Document);
        docVm.IsDirty = false;

        // Refresh metrics after save
        _ = AnalyzeFileMetricsAsync(docVm);
    }

    internal void CloseDocument(DocumentViewModel docVm)
    {
        var index = OpenDocuments.IndexOf(docVm);
        OpenDocuments.Remove(docVm);
        OnPropertyChanged(nameof(HasNoOpenDocuments));

        if (OpenDocuments.Count == 0)
        {
            ActiveDocument = null;
            ActiveTabIndex = -1;
        }
        else if (index >= OpenDocuments.Count)
        {
            ActiveTabIndex = OpenDocuments.Count - 1;
            ActiveDocument = OpenDocuments[ActiveTabIndex];
        }
        else
        {
            ActiveDocument = OpenDocuments[index];
        }
    }

    internal void OnDocumentOpened(object? sender, Document document)
    {
        var docVm = new DocumentViewModel(document);
        docVm.CloseTabCommand = new RelayCommand(() => _ = CloseTabAsync(docVm));
        WireLspCommands(docVm);
        WireBreakpointCommand(docVm);
        OpenDocuments.Add(docVm);
        ActiveDocument = docVm;
        ActiveTabIndex = OpenDocuments.Count - 1;
        OnPropertyChanged(nameof(HasNoOpenDocuments));
        RefreshDocumentDisplayNames();

        _lspSessionManager?.NotifyDocumentOpened(document);

        // Analyze code metrics for C# files
        _ = AnalyzeFileMetricsAsync(docVm);
    }

    /// <summary>
    /// Host-provided confirmation shown before closing a dirty document tab.
    /// When null (tests, headless), dirty tabs close without asking.
    /// </summary>
    public Func<DocumentViewModel, Task<DirtyCloseChoice>>? ConfirmCloseDirtyDocument { get; set; }

    private async Task CloseTabAsync(DocumentViewModel docVm)
    {
        if (docVm.IsDirty && ConfirmCloseDirtyDocument is not null)
        {
            var choice = await ConfirmCloseDirtyDocument(docVm);
            if (choice == DirtyCloseChoice.Cancel)
            {
                return;
            }

            if (choice == DirtyCloseChoice.Save)
            {
                await SaveDocumentAsync(docVm);
            }
        }

        CloseDocument(docVm);
        _ = _editorService.CloseDocumentAsync(docVm.Document);
    }

    internal void OnDocumentClosed(object? sender, Document document)
    {
        var docVm = OpenDocuments.FirstOrDefault(d => d.Document.Id == document.Id);
        if (docVm != null)
        {
            CloseDocument(docVm);
        }

        RefreshDocumentDisplayNames();
        _lspSessionManager?.NotifyDocumentClosed(document);
    }

    /// <summary>
    /// Disambiguates tab titles when several open documents share the same file name
    /// by appending the parent folder, e.g. "Program.cs (Api)".
    /// </summary>
    private void RefreshDocumentDisplayNames()
    {
        foreach (var group in OpenDocuments.GroupBy(d => d.Document.Name))
        {
            if (group.Count() == 1)
            {
                group.First().SetDisplayName(null);
                continue;
            }

            foreach (var docVm in group)
            {
                var folder = docVm.Document.FilePath is { } path
                    ? Path.GetFileName(Path.GetDirectoryName(path))
                    : null;
                docVm.SetDisplayName(string.IsNullOrEmpty(folder)
                    ? null
                    : $"{docVm.Document.Name} ({folder})");
            }
        }
    }

    private void OnActiveDocumentChanged(object? sender, Document document)
    {
        var docVm = OpenDocuments.FirstOrDefault(d => d.Document.Id == document.Id);
        if (docVm != null)
        {
            ActiveDocument = docVm;
            ActiveTabIndex = OpenDocuments.IndexOf(docVm);
        }
    }

    private void OnActiveDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.CursorLine))
        {
            OnPropertyChanged(nameof(CursorLine));
            OnPropertyChanged(nameof(CurrentMethodInfo));
        }
        else if (e.PropertyName is nameof(DocumentViewModel.CursorColumn))
            OnPropertyChanged(nameof(CursorColumn));
        else if (e.PropertyName is nameof(DocumentViewModel.ErrorCount) or nameof(DocumentViewModel.WarningCount))
            NotifyDiagnosticCountsChanged();
        else if (e.PropertyName is nameof(DocumentViewModel.Text))
            DebounceLspDidChange(sender as DocumentViewModel);
    }

    private void DebounceLspDidChange(DocumentViewModel? docVm)
    {
        if (docVm is null || _lspSessionManager is null) return;

        _didChangeCts?.Cancel();
        _didChangeCts?.Dispose();
        var cts = _didChangeCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                _lspSessionManager.NotifyDocumentChanged(docVm.Document, docVm.Text);
            }
            catch (OperationCanceledException) { }
        });
    }

    /// <summary>
    /// Returns a short status string for the method containing the cursor.
    /// </summary>
    private string GetCurrentMethodInfo()
    {
        var metrics = _activeDocument?.FileMethodMetrics;
        if (metrics is null or { Count: 0 }) return "";

        var line = CursorLine;
        // Find the method whose line range contains the cursor
        // Methods are sorted by line; find the last method whose Line ≤ cursor line
        MethodMetrics? current = null;
        foreach (var m in metrics)
        {
            if (m.Line <= line)
                current = m;
            else
                break;
        }

        if (current is null) return "";

        var cc = current.CyclomaticComplexity;
        var mi = current.MaintainabilityIndex;
        var severity = cc switch { <= 5 => "🟢", <= 10 => "🟡", _ => "🔴" };
        return $"{severity} {current.Name} — CC:{cc} MI:{mi:F0}";
    }

    /// <summary>
    /// Analyzes a C# file and sets method metrics on the document view model.
    /// </summary>
    private async Task AnalyzeFileMetricsAsync(DocumentViewModel docVm)
    {
        if (_codeMetricsService is null) return;
        if (docVm.Document.Language != Language.CSharp) return;
        if (string.IsNullOrEmpty(docVm.Document.FilePath)) return;

        try
        {
            var fileMetrics = await _codeMetricsService.CalculateFileMetricsAsync(docVm.Document.FilePath);
            var allMethods = fileMetrics.Types.SelectMany(t => t.Methods).OrderBy(m => m.Line).ToList();
            docVm.FileMethodMetrics = allMethods;
            if (docVm == _activeDocument)
                OnPropertyChanged(nameof(CurrentMethodInfo));
        }
        catch (Exception ex)
        {
            // Metrics analysis failure is non-critical
            Serilog.Log.Debug(ex, "Code metrics analysis failed for {Path}", docVm.Document.FilePath);
        }
    }

    /// <summary>
    /// Immediately sends didChange to the LSP server and waits for the write to complete.
    /// Called before completion/signature help requests so the server sees current text.
    /// </summary>
    private async Task FlushLspDidChangeAsync(DocumentViewModel docVm, string currentText)
    {
        if (_lspSessionManager is null) return;

        _didChangeCts?.Cancel();
        _didChangeCts?.Dispose();
        _didChangeCts = null;

        // Update the document content/version to match what the editor has
        docVm.Document.Content = currentText;
        docVm.Document.Version++;

        await _lspSessionManager.NotifyDocumentChangedAsync(docVm.Document, currentText)
            .ConfigureAwait(false);
    }

    private void OnLspDiagnosticsChanged(object? sender, DocumentDiagnosticsEventArgs args)
    {
        UpdateDiagnostics(args.DocumentUri, args.Diagnostics);
    }

    /// <summary>Set by the view: prompts for a new name for the identifier, returns the new name or null to cancel.</summary>
    public Func<string, Task<string?>>? RequestRenameSymbol { get; set; }

    private static string? GetWordAtCaret(DocumentViewModel docVm)
    {
        var text = docVm.Text;
        if (string.IsNullOrEmpty(text)) return null;

        // Compute offset from 1-based line/column
        var line = 1;
        var offset = 0;
        while (line < docVm.CursorLine && offset < text.Length)
        {
            if (text[offset] == '\n') line++;
            offset++;
        }
        offset += Math.Max(0, docVm.CursorColumn - 1);
        if (offset >= text.Length) offset = text.Length - 1;
        if (offset < 0) return null;

        static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        var start = offset;
        while (start > 0 && IsIdentifierChar(text[start - 1])) start--;
        var end = offset;
        while (end < text.Length && IsIdentifierChar(text[end])) end++;

        return start == end ? null : text[start..end];
    }

    private void WireLspCommands(DocumentViewModel docVm)
    {
        if (_lspSessionManager is null)
            return;

        docVm.HoverFunc = async (line, column, ct) =>
        {
            try
            {
                var pos = new Position { Line = line - 1, Column = column - 1 };
                var hover = await _lspSessionManager.GetHoverAsync(docVm.Document, pos, ct);
                return hover?.Content;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Hover failed");
                return null;
            }
        };

        docVm.RenameSymbolCommand = new AsyncRelayCommand(async () =>
        {
            try
            {
                var word = GetWordAtCaret(docVm);
                if (string.IsNullOrWhiteSpace(word) || RequestRenameSymbol is null)
                {
                    return;
                }

                var newName = await RequestRenameSymbol(word);
                if (string.IsNullOrWhiteSpace(newName) || newName == word)
                {
                    return;
                }

                var pos = new Position { Line = docVm.CursorLine - 1, Column = docVm.CursorColumn - 1 };
                var edit = await _lspSessionManager.RenameAsync(docVm.Document, pos, newName.Trim());
                if (edit is null || edit.Changes.Count == 0)
                {
                    return;
                }

                await _lspSessionManager.ApplyWorkspaceEditAsync(docVm.Document, edit);

                // Reload affected open documents from disk without dirtying them
                foreach (var filePath in edit.Changes.Keys)
                {
                    var affected = OpenDocuments.FirstOrDefault(d =>
                        string.Equals(d.Document.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                    if (affected is not null)
                    {
                        var content = await _fileSystemService.ReadAllTextAsync(filePath);
                        affected.ReplaceTextSilently(content);
                    }
                }

                Serilog.Log.Information("Renamed symbol {Old} to {New} in {Count} file(s)", word, newName, edit.Changes.Count);
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Rename symbol failed");
            }
        });

        docVm.GoToDefinitionCommand = new AsyncRelayCommand(async () =>
        {
            try
            {
                var pos = new Position { Line = docVm.CursorLine - 1, Column = docVm.CursorColumn - 1 };
                var location = await _lspSessionManager.GetDefinitionAsync(docVm.Document, pos);
                if (location is not null)
                {
                    await _editorService.OpenDocumentAsync(location.FilePath);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Go to definition failed (language server unavailable?)");
            }
        });

        docVm.RequestCompletionCommand = new AsyncRelayCommand<LspRequestContext?>(async (ctx) =>
        {
            if (ctx is null) return;
            try
            {
                await FlushLspDidChangeAsync(docVm, ctx.Text).ConfigureAwait(false);
                var pos = new Position { Line = ctx.Line - 1, Column = ctx.Column - 1 };
                var completions = await _lspSessionManager.GetCompletionsAsync(docVm.Document, pos, ctx.TriggerCharacter);
                docVm.LastCompletionResults = completions;
            }
            catch (Exception)
            {
                docVm.LastCompletionResults = [];
            }
        });

        docVm.RequestSignatureHelpCommand = new AsyncRelayCommand<LspRequestContext?>(async (ctx) =>
        {
            if (ctx is null) return;
            try
            {
                await FlushLspDidChangeAsync(docVm, ctx.Text).ConfigureAwait(false);
                var pos = new Position { Line = ctx.Line - 1, Column = ctx.Column - 1 };
                var sigHelp = await _lspSessionManager.GetSignatureHelpAsync(docVm.Document, pos, ctx.TriggerCharacter);
                docVm.LastSignatureHelp = sigHelp;
            }
            catch (Exception)
            {
                docVm.LastSignatureHelp = null;
            }
        });

        docVm.QuickFixCommand = new AsyncRelayCommand<LspRequestContext?>(async (ctx) =>
        {
            if (ctx is null) return;
            try
            {
                await FlushLspDidChangeAsync(docVm, ctx.Text).ConfigureAwait(false);

                // Build a range covering the current line
                var line = ctx.Line - 1;
                var range = new Range
                {
                    Start = new Position { Line = line, Column = 0 },
                    End = new Position { Line = line + 1, Column = 0 },
                };

                // Find diagnostics on the current line
                var lineDiags = docVm.Diagnostics
                    .Where(d => d.Range.Start.Line == line)
                    .ToList();

                var actions = await _lspSessionManager.GetCodeActionsAsync(
                    docVm.Document, range, lineDiags);
                docVm.LastCodeActions = actions;
            }
            catch (Exception)
            {
                docVm.LastCodeActions = [];
            }
        });
    }

    private void WireBreakpointCommand(DocumentViewModel docVm)
    {
        if (_breakpointStore is null)
            return;

        docVm.ToggleBreakpointCommand = new RelayCommand<int>(line =>
        {
            var filePath = docVm.Document.FilePath;
            if (string.IsNullOrEmpty(filePath)) return;

            _breakpointStore.ToggleBreakpoint(filePath, line);
            RefreshBreakpoints(docVm);
        });

        // Load existing breakpoints for this file
        RefreshBreakpoints(docVm);
    }

    /// <summary>
    /// Applies a code action's workspace edit to files and refreshes open documents.
    /// Called from the UI after the user selects a code action.
    /// </summary>
    internal async Task ApplyCodeActionAsync(CodeAction action)
    {
        if (action.Edit is null || _lspSessionManager is null)
            return;

        var activeDoc = ActiveDocument;
        if (activeDoc is null) return;

        await _lspSessionManager.ApplyWorkspaceEditAsync(activeDoc.Document, action.Edit);

        // Reload affected documents that are currently open
        foreach (var filePath in action.Edit.Changes.Keys)
        {
            var openDoc = OpenDocuments.FirstOrDefault(d =>
                string.Equals(d.Document.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            if (openDoc is not null)
            {
                var content = await _fileSystemService.ReadAllTextAsync(filePath);
                openDoc.Text = content;
                openDoc.IsDirty = true;
            }
        }
    }

    private void RefreshBreakpoints(DocumentViewModel docVm)
    {
        if (_breakpointStore is null || string.IsNullOrEmpty(docVm.Document.FilePath)) return;

        var bps = _breakpointStore.GetBreakpoints(docVm.Document.FilePath);
        docVm.Breakpoints = bps.Select(b => (b.Line, b.IsVerified)).ToList();
    }

    internal void NotifyDiagnosticCountsChanged()
    {
        OnPropertyChanged(nameof(TotalErrors));
        OnPropertyChanged(nameof(TotalWarnings));
        OnPropertyChanged(nameof(DiagnosticSummary));
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>User choice when asked about closing a dirty document tab.</summary>
public enum DirtyCloseChoice
{
    Save,
    Discard,
    Cancel,
}

public class DocumentViewModel : INotifyPropertyChanged
{
    public Document Document { get; }

    private string _text;
    private bool _isDirty;
    private int _cursorLine = 1;
    private int _cursorColumn = 1;
    private ICommand? _undoCommand;
    private ICommand? _redoCommand;
    private ICommand? _openSearchCommand;
    private ICommand? _openReplaceCommand;
    private ICommand? _goToDefinitionCommand;
    private ICommand? _requestCompletionCommand;
    private IReadOnlyList<Diagnostic> _diagnostics = [];
    private IReadOnlyList<CompletionItem>? _lastCompletionResults;
    private IReadOnlyList<(int Line, bool Verified)> _breakpoints = [];
    private ICommand? _toggleBreakpointCommand;
    private ICommand? _requestSignatureHelpCommand;
    private SignatureHelp? _lastSignatureHelp;
    private int? _debugCurrentLine;
    private ICommand? _quickFixCommand;
    private IReadOnlyList<CodeAction>? _lastCodeActions;
    private IReadOnlyList<MethodMetrics>? _fileMethodMetrics;
    private Func<string, CancellationToken, Task<string?>>? _debugEvaluateFunc;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DocumentViewModel(Document document)
    {
        Document = document;
        _text = document.Content;
    }

    public ICommand? CloseTabCommand { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                Document.Content = value;
                Document.Version++;
                IsDirty = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
            }
        }
    }

    public int CursorLine
    {
        get => _cursorLine;
        set
        {
            _cursorLine = value;
            OnPropertyChanged();
        }
    }

    public int CursorColumn
    {
        get => _cursorColumn;
        set
        {
            _cursorColumn = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<Diagnostic> Diagnostics
    {
        get => _diagnostics;
        set
        {
            _diagnostics = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ErrorCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(InfoCount));
        }
    }

    public int ErrorCount => _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    public int WarningCount => _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
    public int InfoCount => _diagnostics.Count(d => d.Severity is DiagnosticSeverity.Information or DiagnosticSeverity.Hint);

    public string Title => IsDirty ? $"{DisplayName} *" : DisplayName;

    /// <summary>Tab display name — includes a folder hint when several open documents share a file name.</summary>
    public string DisplayName => _displayName ?? Document.Name;

    private string? _displayName;

    internal void SetDisplayName(string? displayName)
    {
        if (_displayName != displayName)
        {
            _displayName = displayName;
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Title));
        }
    }
    public string Tooltip => Document.FilePath ?? Document.Name;
    public Language Language => Document.Language;

    /// <summary>
    /// Set by RequestCompletionCommand; the behavior reads this to show the CompletionWindow.
    /// </summary>
    public IReadOnlyList<CompletionItem>? LastCompletionResults
    {
        get => _lastCompletionResults;
        set
        {
            _lastCompletionResults = value;
            OnPropertyChanged();
        }
    }

    public ICommand? UndoCommand
    {
        get => _undoCommand;
        set
        {
            _undoCommand = value;
            OnPropertyChanged();
        }
    }

    public ICommand? RedoCommand
    {
        get => _redoCommand;
        set
        {
            _redoCommand = value;
            OnPropertyChanged();
        }
    }

    public ICommand? OpenSearchCommand
    {
        get => _openSearchCommand;
        set
        {
            _openSearchCommand = value;
            OnPropertyChanged();
        }
    }

    public ICommand? OpenReplaceCommand
    {
        get => _openReplaceCommand;
        set
        {
            _openReplaceCommand = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Selection/caret adapter set by the editor behavior (used by inline AI edit).</summary>
    public Behaviors.IEditorSelection? Selection { get; set; }

    /// <summary>LSP hover provider: (line, column, ct) → documentation text or null.</summary>
    public Func<int, int, CancellationToken, Task<string?>>? HoverFunc { get; set; }

    /// <summary>Rename symbol at caret (F2).</summary>
    public ICommand? RenameSymbolCommand { get; set; }

    /// <summary>Replaces the document text without marking it dirty (used after external edits like rename).</summary>
    public void ReplaceTextSilently(string text)
    {
        _text = text;
        Document.Content = text;
        Document.Version++;
        IsDirty = false;
        OnPropertyChanged(nameof(Text));
    }

    public ICommand? GoToDefinitionCommand
    {
        get => _goToDefinitionCommand;
        set
        {
            _goToDefinitionCommand = value;
            OnPropertyChanged();
        }
    }

    public ICommand? RequestCompletionCommand
    {
        get => _requestCompletionCommand;
        set
        {
            _requestCompletionCommand = value;
            OnPropertyChanged();
        }
    }

    public ICommand? RequestSignatureHelpCommand
    {
        get => _requestSignatureHelpCommand;
        set
        {
            _requestSignatureHelpCommand = value;
            OnPropertyChanged();
        }
    }

    public SignatureHelp? LastSignatureHelp
    {
        get => _lastSignatureHelp;
        set
        {
            _lastSignatureHelp = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<(int Line, bool Verified)> Breakpoints
    {
        get => _breakpoints;
        set
        {
            _breakpoints = value;
            OnPropertyChanged();
        }
    }

    public ICommand? ToggleBreakpointCommand
    {
        get => _toggleBreakpointCommand;
        set
        {
            _toggleBreakpointCommand = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The line where the debugger is currently stopped. Null when not paused.
    /// The behavior uses this to render a yellow highlight on the current execution line.
    /// </summary>
    public int? DebugCurrentLine
    {
        get => _debugCurrentLine;
        set
        {
            if (_debugCurrentLine != value)
            {
                _debugCurrentLine = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand? QuickFixCommand
    {
        get => _quickFixCommand;
        set
        {
            _quickFixCommand = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Set by QuickFixCommand; the view reads this to show the code actions menu.
    /// </summary>
    public IReadOnlyList<CodeAction>? LastCodeActions
    {
        get => _lastCodeActions;
        set
        {
            _lastCodeActions = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Method metrics for the current file. Used by the gutter margin to show complexity dots
    /// and by the status bar to display current method info.
    /// </summary>
    public IReadOnlyList<MethodMetrics>? FileMethodMetrics
    {
        get => _fileMethodMetrics;
        set
        {
            _fileMethodMetrics = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Callback for evaluating expressions during debug hover.
    /// Set by MainViewModel when debugging is paused, cleared when continued/stopped.
    /// </summary>
    public Func<string, CancellationToken, Task<string?>>? DebugEvaluateFunc
    {
        get => _debugEvaluateFunc;
        set
        {
            _debugEvaluateFunc = value;
            OnPropertyChanged();
        }
    }

    private Func<int, int, string, string, CancellationToken, Task<string?>>? _inlineCompletionFunc;

    /// <summary>
    /// Function for inline ghost-text completions.
    /// Parameters: line, column, prefix, suffix, cancellationToken → completion text.
    /// Set by MainViewModel when inline completions are enabled.
    /// </summary>
    public Func<int, int, string, string, CancellationToken, Task<string?>>? InlineCompletionFunc
    {
        get => _inlineCompletionFunc;
        set
        {
            _inlineCompletionFunc = value;
            OnPropertyChanged();
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Context passed from the editor behavior to LSP commands with fresh text and position
/// read directly from the TextEditor, avoiding stale binding values.
/// </summary>
public record LspRequestContext(int Line, int Column, string Text, string? TriggerCharacter = null);
