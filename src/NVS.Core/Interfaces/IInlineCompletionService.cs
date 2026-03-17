namespace NVS.Core.Interfaces;

/// <summary>
/// Provides LLM-powered inline code completions (ghost text).
/// </summary>
public interface IInlineCompletionService
{
    /// <summary>
    /// Get an inline completion suggestion for the given code context.
    /// Returns null if no suggestion is available.
    /// </summary>
    Task<string?> GetInlineCompletionAsync(
        string filePath,
        int line,
        int column,
        string prefix,
        string suffix,
        string language,
        CancellationToken cancellationToken = default);
}
