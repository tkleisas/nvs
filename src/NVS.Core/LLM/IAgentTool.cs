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

    /// <summary>Execute the tool with the given JSON arguments.</summary>
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default);
}
