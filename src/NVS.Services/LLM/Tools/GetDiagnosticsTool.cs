using System.Text.Json;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Returns current LSP diagnostics (errors, warnings) for the workspace.
/// </summary>
public sealed class GetDiagnosticsTool : IAgentTool
{
    private readonly Func<IReadOnlyList<DiagnosticInfo>> _getDiagnostics;

    public string Name => "get_diagnostics";
    public string Description => "Get current code diagnostics (errors, warnings, info) for all open files in the workspace.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "severity": {
                    "type": "string",
                    "description": "Filter by severity: error, warning, info, hint. Omit to get all.",
                    "enum": ["error", "warning", "info", "hint"]
                }
            }
        }
        """);

    public GetDiagnosticsTool(Func<IReadOnlyList<DiagnosticInfo>> getDiagnostics)
    {
        _getDiagnostics = getDiagnostics;
    }

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        var severityFilter = args.TryGetProperty("severity", out var s) ? s.GetString() : null;

        var diagnostics = _getDiagnostics();

        if (!string.IsNullOrEmpty(severityFilter))
        {
            diagnostics = diagnostics
                .Where(d => d.Severity.Equals(severityFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var result = JsonSerializer.Serialize(new
        {
            total = diagnostics.Count,
            diagnostics = diagnostics.Select(d => new
            {
                file = d.FilePath,
                line = d.Line,
                column = d.Column,
                severity = d.Severity,
                message = d.Message,
                code = d.Code
            })
        });

        return Task.FromResult(result);
    }

    /// <summary>Diagnostic data transfer object.</summary>
    public sealed record DiagnosticInfo
    {
        public required string FilePath { get; init; }
        public required int Line { get; init; }
        public required int Column { get; init; }
        public required string Severity { get; init; }
        public required string Message { get; init; }
        public string? Code { get; init; }
    }
}
