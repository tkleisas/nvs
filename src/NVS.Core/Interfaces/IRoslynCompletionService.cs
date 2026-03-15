using NVS.Core.Models;
using Range = NVS.Core.Models.Range;

namespace NVS.Core.Interfaces;

/// <summary>
/// Provides Roslyn-powered C# language features using MSBuildWorkspace.
/// Replaces csharp-ls for completions, hover, go-to-definition, references,
/// diagnostics, document symbols, and signature help.
/// </summary>
public interface IRoslynCompletionService : IAsyncDisposable
{
    bool IsWorkspaceLoaded { get; }

    Task LoadWorkspaceAsync(string solutionOrProjectPath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        string filePath, int line, int column, CancellationToken cancellationToken = default);

    Task<HoverInfo?> GetHoverAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    Task<Location?> GetDefinitionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Location>> GetReferencesAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string filePath, CancellationToken cancellationToken = default);

    Task<SignatureHelp?> GetSignatureHelpAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TextEdit>> GetFormattingEditsAsync(string filePath, CancellationToken cancellationToken = default);

    void UpdateDocumentContent(string filePath, string content);

    void UnloadWorkspace();
}
