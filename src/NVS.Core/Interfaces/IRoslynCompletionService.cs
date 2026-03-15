using NVS.Core.Models;

namespace NVS.Core.Interfaces;

/// <summary>
/// Provides Roslyn-powered C# code completions using MSBuildWorkspace.
/// Used alongside csharp-ls (which handles diagnostics/navigation/hover).
/// </summary>
public interface IRoslynCompletionService : IAsyncDisposable
{
    /// <summary>
    /// Whether a Roslyn workspace is currently loaded and ready for completion requests.
    /// </summary>
    bool IsWorkspaceLoaded { get; }

    /// <summary>
    /// Loads a solution (.sln/.slnx) or project (.csproj) into the Roslyn workspace.
    /// Must be called before GetCompletionsAsync will work.
    /// </summary>
    Task LoadWorkspaceAsync(string solutionOrProjectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns C# completions at the given position using Roslyn's CompletionService.
    /// Returns empty list if workspace is not loaded or file is not found.
    /// </summary>
    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        string filePath, int line, int column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the in-memory document content to match the editor's current text.
    /// Call this on document changes so completions reflect the latest edits.
    /// </summary>
    void UpdateDocumentContent(string filePath, string content);

    /// <summary>
    /// Unloads the current workspace and frees resources.
    /// </summary>
    void UnloadWorkspace();
}
