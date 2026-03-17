using System.Text.Json;
using FluentAssertions;
using NVS.Core.LLM;

namespace NVS.Core.Tests;

public sealed class ChatCompletionMessageTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void Serialize_TextOnly_ContentIsString()
    {
        var msg = ChatCompletionMessage.User("Hello");

        var json = JsonSerializer.Serialize(msg, JsonOptions);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("role").GetString().Should().Be("user");
        doc.RootElement.GetProperty("content").ValueKind.Should().Be(JsonValueKind.String);
        doc.RootElement.GetProperty("content").GetString().Should().Be("Hello");
    }

    [Fact]
    public void Serialize_WithImages_ContentIsArray()
    {
        var msg = ChatCompletionMessage.UserWithImages("Describe this", ["data:image/png;base64,iVBOR"]);

        var json = JsonSerializer.Serialize(msg, JsonOptions);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("content").ValueKind.Should().Be(JsonValueKind.Array);
        var parts = doc.RootElement.GetProperty("content");
        parts.GetArrayLength().Should().Be(2);
        parts[0].GetProperty("type").GetString().Should().Be("text");
        parts[0].GetProperty("text").GetString().Should().Be("Describe this");
        parts[1].GetProperty("type").GetString().Should().Be("image_url");
        parts[1].GetProperty("image_url").GetProperty("url").GetString().Should().Be("data:image/png;base64,iVBOR");
    }

    [Fact]
    public void Serialize_SystemMessage_HasRoleAndContent()
    {
        var msg = ChatCompletionMessage.System("You are helpful.");

        var json = JsonSerializer.Serialize(msg, JsonOptions);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("role").GetString().Should().Be("system");
        doc.RootElement.GetProperty("content").GetString().Should().Be("You are helpful.");
    }

    [Fact]
    public void Serialize_ToolResult_HasToolCallIdAndContent()
    {
        var msg = ChatCompletionMessage.ToolResult("call_123", "result data");

        var json = JsonSerializer.Serialize(msg, JsonOptions);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("role").GetString().Should().Be("tool");
        doc.RootElement.GetProperty("tool_call_id").GetString().Should().Be("call_123");
        doc.RootElement.GetProperty("content").GetString().Should().Be("result data");
    }

    [Fact]
    public void Deserialize_TextOnlyContent_ReadAsString()
    {
        var json = """{"role":"user","content":"Hello world"}""";

        var msg = JsonSerializer.Deserialize<ChatCompletionMessage>(json, JsonOptions);

        msg.Should().NotBeNull();
        msg!.Role.Should().Be("user");
        msg.Content.Should().Be("Hello world");
        msg.ContentParts.Should().BeNull();
    }

    [Fact]
    public void Deserialize_ArrayContent_ReadAsContentParts()
    {
        var json = """
        {
            "role": "user",
            "content": [
                {"type": "text", "text": "What is this?"},
                {"type": "image_url", "image_url": {"url": "data:image/png;base64,abc123"}}
            ]
        }
        """;

        var msg = JsonSerializer.Deserialize<ChatCompletionMessage>(json, JsonOptions);

        msg.Should().NotBeNull();
        msg!.Role.Should().Be("user");
        msg.Content.Should().BeNull();
        msg.ContentParts.Should().HaveCount(2);

        var textPart = msg.ContentParts![0] as TextContentPart;
        textPart.Should().NotBeNull();
        textPart!.Text.Should().Be("What is this?");

        var imgPart = msg.ContentParts[1] as ImageContentPart;
        imgPart.Should().NotBeNull();
        imgPart!.ImageUrl.Url.Should().Be("data:image/png;base64,abc123");
    }

    [Fact]
    public void Deserialize_NullContent_HandledGracefully()
    {
        var json = """{"role":"assistant","content":null}""";

        var msg = JsonSerializer.Deserialize<ChatCompletionMessage>(json, JsonOptions);

        msg.Should().NotBeNull();
        msg!.Content.Should().BeNull();
        msg.ContentParts.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_TextOnly_PreservesContent()
    {
        var original = ChatCompletionMessage.User("Round trip test");

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ChatCompletionMessage>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Role.Should().Be("user");
        deserialized.Content.Should().Be("Round trip test");
    }

    [Fact]
    public void RoundTrip_WithImages_PreservesContentParts()
    {
        var original = ChatCompletionMessage.UserWithImages("Analyze", ["data:image/jpeg;base64,abc"]);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ChatCompletionMessage>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.ContentParts.Should().HaveCount(2);
        (deserialized.ContentParts![0] as TextContentPart)!.Text.Should().Be("Analyze");
        (deserialized.ContentParts[1] as ImageContentPart)!.ImageUrl.Url.Should().Be("data:image/jpeg;base64,abc");
    }

    [Fact]
    public void Serialize_MultipleImages_AllIncludedInArray()
    {
        var msg = ChatCompletionMessage.UserWithImages("Compare these",
            ["data:image/png;base64,img1", "data:image/png;base64,img2"]);

        var json = JsonSerializer.Serialize(msg, JsonOptions);
        var doc = JsonDocument.Parse(json);

        var parts = doc.RootElement.GetProperty("content");
        parts.GetArrayLength().Should().Be(3); // 1 text + 2 images
    }

    [Fact]
    public void Serialize_AssistantWithReasoningContent_IncludesIt()
    {
        var msg = ChatCompletionMessage.Assistant("answer");
        msg.ReasoningContent = "thinking...";

        var json = JsonSerializer.Serialize(msg, JsonOptions);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("reasoning_content").GetString().Should().Be("thinking...");
    }
}
