using System.Text.Json;
using System.Text.Json.Serialization;

namespace NVS.Core.LLM;

/// <summary>
/// OpenAI-compatible tool definition for function calling.
/// </summary>
public sealed class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public required FunctionDefinition Function { get; set; }
}

/// <summary>
/// Function schema within a tool definition.
/// </summary>
public sealed class FunctionDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; set; }
}

/// <summary>
/// A tool call requested by the LLM in its response.
/// </summary>
public sealed class ToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public ToolCallFunction Function { get; set; } = new();
}

/// <summary>
/// Function invocation details within a tool call.
/// </summary>
public sealed class ToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;
}
