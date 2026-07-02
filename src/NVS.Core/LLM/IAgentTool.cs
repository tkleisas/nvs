using System.Text.Json;

namespace NVS.Core.LLM;

/// <summary>
/// Interface for agent tools that can be called by the LLM during the agent loop.
/// </summary>
public interface IAgentTool
{
    /// <summary>Tool name as sent to the LLM (e.g., "read_file", "write_file").</summary>
    string Name { get; }

    /// <summary>Description for the LLM to understand when to use this tool.</summary>
    string Description { get; }

    /// <summary>JSON Schema for the tool's parameters.</summary>
    JsonElement ParameterSchema { get; }

    /// <summary>
    /// Whether the tool performs a destructive or outward-facing action (running commands,
    /// writing files) and must be approved by the user before each execution.
    /// </summary>
    bool RequiresApproval => false;

    /// <summary>Execute the tool with the given JSON arguments.</summary>
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default);
}

/// <summary>A request for the user to approve a destructive tool call.</summary>
public sealed record ToolApprovalRequest
{
    /// <summary>Tool name (e.g., "run_terminal").</summary>
    public required string ToolName { get; init; }

    /// <summary>Human-readable tool description.</summary>
    public required string Description { get; init; }

    /// <summary>Raw JSON arguments the LLM wants to invoke the tool with.</summary>
    public required string Arguments { get; init; }
}
