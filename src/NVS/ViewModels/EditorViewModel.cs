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

    public EditorViewModel(IEditorService editorService, IFileSystemService fileSystemService)
    {
        _editorService = editorService;
        _fileSystemService = fileSystemService;

        _editorService.DocumentOpened += OnDocumentOpened;
        _editorService.DocumentClosed += OnDocumentClosed;
        _editorService.ActiveDocumentChanged += OnActiveDocumentChanged;
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
        OpenDocuments.Add(docVm);
        ActiveDocument = docVm;
        ActiveTabIndex = OpenDocuments.Count - 1;
        OnPropertyChanged(nameof(HasNoOpenDocuments));
    }

    private void OnDocumentClosed(object? sender, Document document)
    {
        var docVm = OpenDocuments.FirstOrDefault(d => d.Document.Id == document.Id);
        if (docVm != null)
        {
            CloseDocument(docVm);
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
            OnPropertyChanged(nameof(CursorLine));
        else if (e.PropertyName is nameof(DocumentViewModel.CursorColumn))
            OnPropertyChanged(nameof(CursorColumn));
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public DocumentViewModel(Document document)
    {
        Document = document;
        _text = document.Content;
    }

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

    public string Title => IsDirty ? $"{Document.Name} *" : Document.Name;
    public string Tooltip => Document.FilePath ?? Document.Name;
    public Language Language => Document.Language;

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

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
