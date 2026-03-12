using NVS.Core.Enums;
using NVS.Core.Models;
using Range = NVS.Core.Models.Range;

namespace NVS.Core.Interfaces;

public interface ILspClient
{
    bool IsConnected { get; }
    Language Language { get; }
    
    Task InitializeAsync(string rootPath, CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<SignatureHelp?> GetSignatureHelpAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<HoverInfo?> GetHoverAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<Location?> GetDefinitionAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Location>> GetReferencesAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(Document document, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TextEdit>> GetFormattingEditsAsync(Document document, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(Document document, CancellationToken cancellationToken = default);
    
    void NotifyDocumentOpened(Document document);
    void NotifyDocumentChanged(Document document, string content);
    void NotifyDocumentClosed(Document document);
    void NotifyDocumentSaved(Document document);
    
    event EventHandler<DocumentDiagnosticsEventArgs>? DiagnosticsReceived;
}

public sealed record CompletionItem
{
    public required string Label { get; init; }
    public string? Detail { get; init; }
    public string? Documentation { get; init; }
    public CompletionItemKind Kind { get; init; }
    public string? InsertText { get; init; }
    public bool IsSnippet { get; init; }
}

public enum CompletionItemKind
{
    Text,
    Method,
    Function,
    Constructor,
    Field,
    Variable,
    Class,
    Interface,
    Module,
    Property,
    Keyword,
    Snippet
}

public sealed record HoverInfo
{
    public required string Content { get; init; }
    public Range? Range { get; init; }
}

public sealed record DocumentSymbol
{
    public required string Name { get; init; }
    public SymbolKind Kind { get; init; }
    public Range Range { get; init; } = Range.Empty;
    public Range SelectionRange { get; init; } = Range.Empty;
    public string? Detail { get; init; }
    public IReadOnlyList<DocumentSymbol> Children { get; init; } = [];
}

public sealed record Diagnostic
{
    public required string Message { get; init; }
    public required DiagnosticSeverity Severity { get; init; }
    public required Range Range { get; init; }
    public string? Source { get; init; }
    public string? Code { get; init; }
}

public sealed record SignatureHelp
{
    public IReadOnlyList<SignatureInformation> Signatures { get; init; } = [];
    public int ActiveSignature { get; init; }
    public int ActiveParameter { get; init; }
}

public sealed record SignatureInformation
{
    public required string Label { get; init; }
    public string? Documentation { get; init; }
    public IReadOnlyList<ParameterInformation> Parameters { get; init; } = [];
}

public sealed record ParameterInformation
{
    public required string Label { get; init; }
    public string? Documentation { get; init; }
}

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Information,
    Hint
}
