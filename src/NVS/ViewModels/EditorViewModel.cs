using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.ViewModels;

public partial class EditorViewModel : INotifyPropertyChanged
{
    private readonly IEditorService _editorService;
    private readonly IFileSystemService _fileSystemService;
    private readonly ILspSessionManager? _lspSessionManager;
    private readonly IBreakpointStore? _breakpointStore;

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

            OnPropertyChanged();
            OnPropertyChanged(nameof(CursorLine));
            OnPropertyChanged(nameof(CursorColumn));
        }
    }

    public int CursorLine => _activeDocument?.CursorLine ?? 1;
    public int CursorColumn => _activeDocument?.CursorColumn ?? 1;

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

    public ObservableCollection<DocumentViewModel> OpenDocuments { get; } = [];

    public EditorViewModel(IEditorService editorService, IFileSystemService fileSystemService, ILspSessionManager? lspSessionManager = null, IBreakpointStore? breakpointStore = null)
    {
        _editorService = editorService;
        _fileSystemService = fileSystemService;
        _lspSessionManager = lspSessionManager;
        _breakpointStore = breakpointStore;

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
        CloseDocument(ActiveDocument);
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
    }

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
    }

    private void CloseDocument(DocumentViewModel docVm)
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

    private void OnDocumentOpened(object? sender, Document document)
    {
        var docVm = new DocumentViewModel(document);
        docVm.CloseTabCommand = new RelayCommand(() =>
        {
            CloseDocument(docVm);
            _ = _editorService.CloseDocumentAsync(docVm.Document);
        });
        WireLspCommands(docVm);
        WireBreakpointCommand(docVm);
        OpenDocuments.Add(docVm);
        ActiveDocument = docVm;
        ActiveTabIndex = OpenDocuments.Count - 1;
        OnPropertyChanged(nameof(HasNoOpenDocuments));

        _lspSessionManager?.NotifyDocumentOpened(document);
    }

    private void OnDocumentClosed(object? sender, Document document)
    {
        var docVm = OpenDocuments.FirstOrDefault(d => d.Document.Id == document.Id);
        if (docVm != null)
        {
            CloseDocument(docVm);
        }

        _lspSessionManager?.NotifyDocumentClosed(document);
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
            OnPropertyChanged(nameof(CursorLine));
        else if (e.PropertyName is nameof(DocumentViewModel.CursorColumn))
            OnPropertyChanged(nameof(CursorColumn));
        else if (e.PropertyName is nameof(DocumentViewModel.ErrorCount) or nameof(DocumentViewModel.WarningCount))
            NotifyDiagnosticCountsChanged();
    }

    private void OnLspDiagnosticsChanged(object? sender, DocumentDiagnosticsEventArgs args)
    {
        UpdateDiagnostics(args.DocumentUri, args.Diagnostics);
    }

    private void WireLspCommands(DocumentViewModel docVm)
    {
        if (_lspSessionManager is null)
            return;

        docVm.GoToDefinitionCommand = new AsyncRelayCommand(async () =>
        {
            var pos = new Position { Line = docVm.CursorLine - 1, Column = docVm.CursorColumn - 1 };
            var location = await _lspSessionManager.GetDefinitionAsync(docVm.Document, pos);
            if (location is not null)
            {
                await _editorService.OpenDocumentAsync(location.FilePath);
            }
        });

        docVm.RequestCompletionCommand = new AsyncRelayCommand(async () =>
        {
            var pos = new Position { Line = docVm.CursorLine - 1, Column = docVm.CursorColumn - 1 };
            var completions = await _lspSessionManager.GetCompletionsAsync(docVm.Document, pos);
            docVm.LastCompletionResults = completions;
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
    private ICommand? _goToDefinitionCommand;
    private ICommand? _requestCompletionCommand;
    private IReadOnlyList<Diagnostic> _diagnostics = [];
    private IReadOnlyList<CompletionItem>? _lastCompletionResults;
    private IReadOnlyList<(int Line, bool Verified)> _breakpoints = [];
    private ICommand? _toggleBreakpointCommand;
    private int? _debugCurrentLine;

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

    public string Title => IsDirty ? $"{Document.Name} *" : Document.Name;
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

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
