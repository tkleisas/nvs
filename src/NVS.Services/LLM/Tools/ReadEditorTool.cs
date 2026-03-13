using System.Text.Json;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Reads the current editor content, selection, and file info.
/// Uses a delegate to access the UI state from the service layer.
/// </summary>
public sealed class ReadEditorTool : IAgentTool
{
    /// <summary>
    /// Delegate that returns the current editor state.
    /// </summary>
    public sealed record EditorState
    {
        public string? FilePath { get; init; }
        public string? FileName { get; init; }
        public string? Language { get; init; }
        public string Content { get; init; } = string.Empty;
        public string? SelectedText { get; init; }
        public int CursorLine { get; init; }
        public int CursorColumn { get; init; }
    }

    private readonly Func<EditorState?> _getEditorState;

    public string Name => "read_editor";
    public string Description => "Read the currently active editor's content, file path, language, selection, and cursor position.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "include_content": {
                    "type": "boolean",
                    "description": "Include the full file content. Default: true. Set false to get only metadata and selection."
                }
            }
        }
        """);

    public ReadEditorTool(Func<EditorState?> getEditorState)
    {
        _getEditorState = getEditorState;
    }

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var state = _getEditorState();
        if (state is null)
            return Task.FromResult(JsonSerializer.Serialize(new { error = "No editor is currently active" }));

        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        var includeContent = !args.TryGetProperty("include_content", out var ic) || ic.GetBoolean();

        var result = new Dictionary<string, object?>
        {
            ["file_path"] = state.FilePath,
            ["file_name"] = state.FileName,
            ["language"] = state.Language,
            ["cursor_line"] = state.CursorLine,
            ["cursor_column"] = state.CursorColumn,
            ["has_selection"] = !string.IsNullOrEmpty(state.SelectedText),
        };

        if (!string.IsNullOrEmpty(state.SelectedText))
            result["selected_text"] = state.SelectedText;

        if (includeContent)
        {
            result["content"] = state.Content;
            result["total_lines"] = state.Content.Split('\n').Length;
        }

        return Task.FromResult(JsonSerializer.Serialize(result));
    }
}
