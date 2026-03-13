using System.Collections.Concurrent;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.Services.Editor;

public sealed class EditorService : IEditorService
{
    private readonly ConcurrentDictionary<Guid, Document> _documents = new();
    private readonly List<Document> _openDocumentsList = [];
    private Document? _activeDocument;

    public IReadOnlyList<Document> OpenDocuments => _openDocumentsList;
    public Document? ActiveDocument => _activeDocument;

    public event EventHandler<Document>? DocumentOpened;
    public event EventHandler<Document>? DocumentClosed;
    public event EventHandler<Document>? DocumentSaved;
    public event EventHandler<Document>? ActiveDocumentChanged;
    public event EventHandler<Document>? DocumentChanged;

    public async Task<Document> OpenDocumentAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);

        // If the file is already open, just activate it
        var existing = _openDocumentsList.FirstOrDefault(d =>
            string.Equals(Path.GetFullPath(d.FilePath ?? ""), fullPath, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            SetActiveDocument(existing);
            return existing;
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Path = path,
            Name = Path.GetFileName(path),
            FilePath = fullPath,
            State = DocumentState.Loading,
            Language = DetectLanguage(path)
        };

        _documents[document.Id] = document;
        _openDocumentsList.Add(document);

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            document.Content = content;
            document.State = DocumentState.Loaded;
            document.LastModified = File.GetLastWriteTimeUtc(fullPath);
        }
        catch (Exception)
        {
            document.State = DocumentState.Error;
            throw;
        }

        DocumentOpened?.Invoke(this, document);
        SetActiveDocument(document);

        return document;
    }

    public async Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        if (document.FilePath is null) return;

        document.State = DocumentState.Saving;

        try
        {
            await File.WriteAllTextAsync(document.FilePath, document.Content, cancellationToken);
            document.State = DocumentState.Saved;
            document.IsDirty = false;
            document.LastModified = DateTimeOffset.UtcNow;
            DocumentSaved?.Invoke(this, document);
        }
        catch (Exception)
        {
            document.State = DocumentState.Error;
            throw;
        }
    }

    public async Task SaveAllDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var dirtyDocuments = _openDocumentsList.Where(d => d.IsDirty).ToList();
        foreach (var document in dirtyDocuments)
        {
            await SaveDocumentAsync(document, cancellationToken);
        }
    }

    public Task CloseDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        _documents.TryRemove(document.Id, out _);
        _openDocumentsList.Remove(document);

        if (_activeDocument == document)
        {
            _activeDocument = _openDocumentsList.FirstOrDefault();
            ActiveDocumentChanged?.Invoke(this, _activeDocument!);
        }

        DocumentClosed?.Invoke(this, document);
        return Task.CompletedTask;
    }

    public async Task CloseAllDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var documents = _openDocumentsList.ToList();
        foreach (var document in documents)
        {
            await CloseDocumentAsync(document, cancellationToken);
        }
    }

    public void ApplyEdit(Document document, TextEdit edit)
    {
        var lines = document.Content.Split('\n');
        var startLine = edit.Range.Start.Line;
        var startCol = edit.Range.Start.Column;

        var content = document.Content;
        var startPos = GetPosition(content, startLine, startCol);
        var endPos = GetPosition(content, edit.Range.End.Line, edit.Range.End.Column);

        document.Content = content[..startPos] + edit.NewText + content[endPos..];
        document.IsDirty = true;
        document.Version++;
        DocumentChanged?.Invoke(this, document);
    }

    public void ApplyEdits(Document document, IEnumerable<TextEdit> edits)
    {
        foreach (var edit in edits.OrderByDescending(e => e.Range.Start.Line)
                     .ThenByDescending(e => e.Range.Start.Column))
        {
            ApplyEdit(document, edit);
        }
    }

    public void SetActiveDocument(Document? document)
    {
        if (document == _activeDocument) return;
        _activeDocument = document;
        ActiveDocumentChanged?.Invoke(this, document!);
    }

    private static int GetPosition(string content, int line, int column)
    {
        var currentLine = 0;
        var currentCol = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (currentLine == line && currentCol == column)
                return i;

            if (content[i] == '\n')
            {
                currentLine++;
                currentCol = 0;
            }
            else
            {
                currentCol++;
            }
        }

        return content.Length;
    }

    private static Language DetectLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => Language.CSharp,
            ".cpp" or ".cc" or ".cxx" or ".hpp" or ".h" => Language.Cpp,
            ".c" => Language.C,
            ".js" => Language.JavaScript,
            ".ts" => Language.TypeScript,
            ".json" => Language.Json,
            ".xml" => Language.Xml,
            ".html" or ".htm" => Language.Html,
            ".css" => Language.Css,
            ".py" => Language.Python,
            ".rs" => Language.Rust,
            ".go" => Language.Go,
            ".md" => Language.Markdown,
            ".yaml" or ".yml" => Language.Yaml,
            ".toml" => Language.Toml,
            ".sql" => Language.Sql,
            _ => Language.Unknown
        };
    }
}
