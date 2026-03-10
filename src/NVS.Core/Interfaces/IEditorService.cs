using NVS.Core.Models;

namespace NVS.Core.Interfaces;

public interface IEditorService
{
    IReadOnlyList<Document> OpenDocuments { get; }
    Document? ActiveDocument { get; }
    
    Task<Document> OpenDocumentAsync(string path, CancellationToken cancellationToken = default);
    Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default);
    Task SaveAllDocumentsAsync(CancellationToken cancellationToken = default);
    Task CloseDocumentAsync(Document document, CancellationToken cancellationToken = default);
    Task CloseAllDocumentsAsync(CancellationToken cancellationToken = default);
    
    void ApplyEdit(Document document, TextEdit edit);
    void ApplyEdits(Document document, IEnumerable<TextEdit> edits);
    
    event EventHandler<Document>? DocumentOpened;
    event EventHandler<Document>? DocumentClosed;
    event EventHandler<Document>? DocumentSaved;
    event EventHandler<Document>? ActiveDocumentChanged;
    event EventHandler<Document>? DocumentChanged;
}
