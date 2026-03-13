using System.Text.Json;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Applies a text edit to the current editor.
/// Supports replacing selection, inserting at cursor, or replacing a line range.
/// </summary>
public sealed class ApplyEditTool : IAgentTool
{
    /// <summary>Delegate to apply an edit to the active editor.</summary>
    public sealed record EditOperation
    {
        /// <summary>New text to insert/replace.</summary>
        public required string NewText { get; init; }

        /// <summary>If set, replace lines from this line number (1-based).</summary>
        public int? LineStart { get; init; }

        /// <summary>If set, replace lines up to this line number (1-based, inclusive).</summary>
        public int? LineEnd { get; init; }

        /// <summary>If true, replace the current selection instead of line range.</summary>
        public bool ReplaceSelection { get; init; }
    }

    private readonly Func<EditOperation, bool> _applyEdit;

    public string Name => "apply_edit";
    public string Description => "Apply a text edit to the active editor. Can replace the current selection, insert at cursor, or replace a specific line range.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "new_text": {
                    "type": "string",
                    "description": "The new text to insert or replace with"
                },
                "line_start": {
                    "type": "integer",
                    "description": "Start line (1-based) for range replacement. Omit to replace selection or insert at cursor."
                },
                "line_end": {
                    "type": "integer",
                    "description": "End line (1-based, inclusive) for range replacement."
                },
                "replace_selection": {
                    "type": "boolean",
                    "description": "If true, replace the current editor selection. Default: false."
                }
            },
            "required": ["new_text"]
        }
        """);

    public ApplyEditTool(Func<EditOperation, bool> applyEdit)
    {
        _applyEdit = applyEdit;
    }

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        var newText = args.GetProperty("new_text").GetString() ?? string.Empty;

        var operation = new EditOperation
        {
            NewText = newText,
            LineStart = args.TryGetProperty("line_start", out var ls) ? ls.GetInt32() : null,
            LineEnd = args.TryGetProperty("line_end", out var le) ? le.GetInt32() : null,
            ReplaceSelection = args.TryGetProperty("replace_selection", out var rs) && rs.GetBoolean()
        };

        var success = _applyEdit(operation);

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            success,
            action = operation.ReplaceSelection ? "replaced_selection"
                : operation.LineStart.HasValue ? $"replaced_lines_{operation.LineStart}-{operation.LineEnd}"
                : "inserted_at_cursor"
        }));
    }
}
