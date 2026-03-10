using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
            _activeDocument = value;
            OnPropertyChanged();
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
    private async Task NewFile()
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
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        await Task.CompletedTask;
        // TODO: Implement file dialog via Avalonia
    }

    public async Task OpenFileAsync(string filePath)
    {
        var existing = OpenDocuments.FirstOrDefault(d => d.Document.FilePath == filePath);
        if (existing != null)
        {
            ActiveDocument = existing;
            ActiveTabIndex = OpenDocuments.IndexOf(existing);
            return;
        }

        try
        {
            var document = await _editorService.OpenDocumentAsync(filePath);
            var docVm = new DocumentViewModel(document);
            OpenDocuments.Add(docVm);
            ActiveDocument = docVm;
            ActiveTabIndex = OpenDocuments.Count - 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to open file: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveFile()
    {
        if (ActiveDocument?.Document == null) return;
        await SaveDocumentAsync(ActiveDocument);
    }

    [RelayCommand]
    private async Task SaveFileAs()
    {
        if (ActiveDocument?.Document == null) return;
        // TODO: Implement save as dialog
        await SaveDocumentAsync(ActiveDocument);
    }

    [RelayCommand]
    private async Task SaveAll()
    {
        foreach (var docVm in OpenDocuments.Where(d => d.IsDirty))
        {
            await SaveDocumentAsync(docVm);
        }
    }

    [RelayCommand]
    private void CloseFile()
    {
        if (ActiveDocument == null) return;
        CloseDocument(ActiveDocument);
    }

    [RelayCommand]
    private void CloseAllFiles()
    {
        foreach (var doc in OpenDocuments.ToList())
        {
            CloseDocument(doc);
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

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
