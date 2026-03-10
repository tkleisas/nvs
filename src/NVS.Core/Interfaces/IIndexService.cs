using NVS.Core.Enums;
using NVS.Core.Models;

namespace NVS.Core.Interfaces;

public interface IIndexService
{
    bool IsIndexing { get; }
    double Progress { get; }
    
    Task IndexWorkspaceAsync(CancellationToken cancellationToken = default);
    Task IndexFileAsync(string path, CancellationToken cancellationToken = default);
    Task RemoveFileAsync(string path, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<Symbol>> SearchSymbolsAsync(string query, int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> SearchFilesAsync(string query, int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SearchResult>> SearchContentAsync(string query, int limit = 100, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<Location>> FindReferencesAsync(Symbol symbol, CancellationToken cancellationToken = default);
    Task<Symbol?> GetSymbolAtPositionAsync(string filePath, int line, int column, CancellationToken cancellationToken = default);
    
    event EventHandler<IndexProgressEventArgs>? ProgressChanged;
    event EventHandler? IndexingCompleted;
}

public sealed record SearchResult
{
    public required string FilePath { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required string Context { get; init; }
    public required string Match { get; init; }
}

public sealed class IndexProgressEventArgs : EventArgs
{
    public required int FilesProcessed { get; init; }
    public required int TotalFiles { get; init; }
    public required string CurrentFile { get; init; }
    public double Progress => TotalFiles > 0 ? (double)FilesProcessed / TotalFiles : 0;
}
