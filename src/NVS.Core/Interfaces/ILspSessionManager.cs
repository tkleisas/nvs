using NVS.Core.Models;

namespace NVS.Core.Interfaces;

/// <summary>
/// Manages LSP client lifecycle per language/workspace combination.
/// Lazily creates clients on first request and relays diagnostics.
/// </summary>
public interface ILspSessionManager : IAsyncDisposable
{
    /// <summary>
    /// Gets an LSP client for the given document's language.
    /// Creates and initializes one if not already active.
    /// Returns null if no language server is configured.
    /// </summary>
    Task<ILspClient?> GetClientAsync(Document document, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<SignatureHelp?> GetSignatureHelpAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<Location?> GetDefinitionAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Location>> GetReferencesAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<HoverInfo?> GetHoverAsync(Document document, Position position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TextEdit>> FormatDocumentAsync(Document document, CancellationToken cancellationToken = default);

    void NotifyDocumentOpened(Document document);
    void NotifyDocumentChanged(Document document, string content);
    void NotifyDocumentClosed(Document document);
    void NotifyDocumentSaved(Document document);

    /// <summary>
    /// Fired when diagnostics are received for a specific document URI.
    /// </summary>
    event EventHandler<DocumentDiagnosticsEventArgs>? DiagnosticsChanged;
}

public sealed class DocumentDiagnosticsEventArgs : EventArgs
{
    public required string DocumentUri { get; init; }
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }
}
