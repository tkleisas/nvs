using System.Text.Json.Serialization;

namespace NVS.Core.LLM;

/// <summary>
/// OpenAI-compatible chat message with tool-calling and multimodal support.
/// </summary>
public sealed class ChatCompletionMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// Reasoning content for thinking models (e.g., DeepSeek-R1).
    /// Not part of the standard OpenAI API.
    /// </summary>
    [JsonPropertyName("reasoning_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; set; }

    public static ChatCompletionMessage System(string content) =>
        new() { Role = "system", Content = content };

    public static ChatCompletionMessage User(string content) =>
        new() { Role = "user", Content = content };

    public static ChatCompletionMessage Assistant(string content) =>
        new() { Role = "assistant", Content = content };

    public static ChatCompletionMessage ToolResult(string toolCallId, string content) =>
        new() { Role = "tool", ToolCallId = toolCallId, Content = content };
}
