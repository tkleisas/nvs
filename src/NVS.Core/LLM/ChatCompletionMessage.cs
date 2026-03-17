using System.Text.Json;
using System.Text.Json.Serialization;

namespace NVS.Core.LLM;

/// <summary>
/// OpenAI-compatible chat message with tool-calling and multimodal support.
/// </summary>
[JsonConverter(typeof(ChatCompletionMessageConverter))]
public sealed class ChatCompletionMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    /// <summary>
    /// Multimodal content parts (text + images). When set, Content is ignored during serialization.
    /// The JSON converter outputs content as a string when only text, or as an array when images are included.
    /// </summary>
    [JsonIgnore]
    public List<ContentPart>? ContentParts { get; set; }

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

    public static ChatCompletionMessage UserWithImages(string text, IEnumerable<string> imageDataUris)
    {
        var parts = new List<ContentPart> { new TextContentPart { Text = text } };
        foreach (var uri in imageDataUris)
            parts.Add(new ImageContentPart { ImageUrl = new ImageUrl { Url = uri } });
        return new() { Role = "user", ContentParts = parts };
    }

    public static ChatCompletionMessage Assistant(string content) =>
        new() { Role = "assistant", Content = content };

    public static ChatCompletionMessage ToolResult(string toolCallId, string content) =>
        new() { Role = "tool", ToolCallId = toolCallId, Content = content };
}

/// <summary>Base type for multimodal content parts.</summary>
[JsonDerivedType(typeof(TextContentPart))]
[JsonDerivedType(typeof(ImageContentPart))]
public abstract class ContentPart
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}

public sealed class TextContentPart : ContentPart
{
    [JsonPropertyName("type")]
    public override string Type => "text";

    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

public sealed class ImageContentPart : ContentPart
{
    [JsonPropertyName("type")]
    public override string Type => "image_url";

    [JsonPropertyName("image_url")]
    public required ImageUrl ImageUrl { get; init; }
}

public sealed class ImageUrl
{
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}

/// <summary>
/// Custom JSON converter that serializes content as a string (text-only) or as an array (multimodal).
/// </summary>
public sealed class ChatCompletionMessageConverter : JsonConverter<ChatCompletionMessage>
{
    public override ChatCompletionMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var msg = new ChatCompletionMessage();

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return msg;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var prop = reader.GetString();
            reader.Read();

            switch (prop)
            {
                case "role":
                    msg.Role = reader.GetString() ?? "user";
                    break;
                case "content":
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        msg.Content = reader.GetString();
                    }
                    else if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        msg.ContentParts = [];
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            var part = JsonSerializer.Deserialize<JsonElement>(ref reader, options);
                            var type = part.GetProperty("type").GetString();
                            if (type == "text")
                            {
                                msg.ContentParts.Add(new TextContentPart { Text = part.GetProperty("text").GetString() ?? "" });
                            }
                            else if (type == "image_url")
                            {
                                var urlObj = part.GetProperty("image_url");
                                msg.ContentParts.Add(new ImageContentPart
                                {
                                    ImageUrl = new ImageUrl
                                    {
                                        Url = urlObj.GetProperty("url").GetString() ?? "",
                                        Detail = urlObj.TryGetProperty("detail", out var d) ? d.GetString() : null
                                    }
                                });
                            }
                        }
                    }
                    else if (reader.TokenType == JsonTokenType.Null)
                    {
                        msg.Content = null;
                    }
                    break;
                case "tool_calls":
                    msg.ToolCalls = JsonSerializer.Deserialize<List<ToolCall>>(ref reader, options);
                    break;
                case "tool_call_id":
                    msg.ToolCallId = reader.GetString();
                    break;
                case "name":
                    msg.Name = reader.GetString();
                    break;
                case "reasoning_content":
                    msg.ReasoningContent = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return msg;
    }

    public override void Write(Utf8JsonWriter writer, ChatCompletionMessage value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("role", value.Role);

        if (value.ContentParts is { Count: > 0 })
        {
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            foreach (var part in value.ContentParts)
            {
                JsonSerializer.Serialize(writer, part, part.GetType(), options);
            }
            writer.WriteEndArray();
        }
        else if (value.Content is not null)
        {
            writer.WriteString("content", value.Content);
        }

        if (value.ToolCalls is { Count: > 0 })
        {
            writer.WritePropertyName("tool_calls");
            JsonSerializer.Serialize(writer, value.ToolCalls, options);
        }

        if (value.ToolCallId is not null)
            writer.WriteString("tool_call_id", value.ToolCallId);

        if (value.Name is not null)
            writer.WriteString("name", value.Name);

        if (value.ReasoningContent is not null)
            writer.WriteString("reasoning_content", value.ReasoningContent);

        writer.WriteEndObject();
    }
}
