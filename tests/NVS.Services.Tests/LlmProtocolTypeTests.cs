using System.Text.Json;
using NVS.Core.LLM;

namespace NVS.Services.Tests;

public sealed class LlmProtocolTypeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void ChatCompletionRequest_ShouldSerialize_WithRequiredFields()
    {
        var request = new ChatCompletionRequest
        {
            Model = "deepseek-chat",
            Messages = [ChatCompletionMessage.User("Hello")],
            Temperature = 0.2,
            MaxTokens = 4096,
            Stream = true
        };

        var json = JsonSerializer.Serialize(request);

        json.Should().Contain("\"model\":\"deepseek-chat\"");
        json.Should().Contain("\"messages\":");
        json.Should().Contain("\"temperature\":0.2");
        json.Should().Contain("\"max_tokens\":4096");
        json.Should().Contain("\"stream\":true");
    }

    [Fact]
    public void ChatCompletionRequest_ShouldOmitNullTools()
    {
        var request = new ChatCompletionRequest
        {
            Model = "test",
            Messages = [ChatCompletionMessage.User("Hi")]
        };

        var json = JsonSerializer.Serialize(request);

        json.Should().NotContain("\"tools\"");
        json.Should().NotContain("\"tool_choice\"");
    }

    [Fact]
    public void ChatCompletionRequest_ShouldSerialize_WithTools()
    {
        var toolParams = JsonSerializer.Deserialize<JsonElement>("""
            {
                "type": "object",
                "properties": {
                    "path": { "type": "string", "description": "File path" }
                },
                "required": ["path"]
            }
            """);

        var request = new ChatCompletionRequest
        {
            Model = "deepseek-chat",
            Messages = [ChatCompletionMessage.User("Read file")],
            Tools =
            [
                new ToolDefinition
                {
                    Function = new FunctionDefinition
                    {
                        Name = "read_file",
                        Description = "Read file contents",
                        Parameters = toolParams
                    }
                }
            ],
            ToolChoice = "auto"
        };

        var json = JsonSerializer.Serialize(request);

        json.Should().Contain("\"tools\":");
        json.Should().Contain("\"read_file\"");
        json.Should().Contain("\"tool_choice\":\"auto\"");
    }

    [Fact]
    public void ChatCompletionResponse_ShouldDeserialize_WithContent()
    {
        var json = """
            {
                "id": "chatcmpl-123",
                "object": "chat.completion",
                "model": "deepseek-chat",
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": "Hello! How can I help?"
                        },
                        "finish_reason": "stop"
                    }
                ],
                "usage": {
                    "prompt_tokens": 10,
                    "completion_tokens": 8,
                    "total_tokens": 18
                }
            }
            """;

        var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json);

        response.Should().NotBeNull();
        response!.Id.Should().Be("chatcmpl-123");
        response.Model.Should().Be("deepseek-chat");
        response.Choices.Should().HaveCount(1);
        response.Choices[0].Message!.Role.Should().Be("assistant");
        response.Choices[0].Message!.Content.Should().Be("Hello! How can I help?");
        response.Choices[0].FinishReason.Should().Be("stop");
        response.Usage!.PromptTokens.Should().Be(10);
        response.Usage.CompletionTokens.Should().Be(8);
        response.Usage.TotalTokens.Should().Be(18);
    }

    [Fact]
    public void ChatCompletionResponse_ShouldDeserialize_WithToolCalls()
    {
        var json = """
            {
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": null,
                            "tool_calls": [
                                {
                                    "id": "call_abc123",
                                    "type": "function",
                                    "function": {
                                        "name": "read_file",
                                        "arguments": "{\"path\": \"src/main.cs\"}"
                                    }
                                }
                            ]
                        },
                        "finish_reason": "tool_calls"
                    }
                ]
            }
            """;

        var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json);

        response.Should().NotBeNull();
        var message = response!.Choices[0].Message!;
        message.Content.Should().BeNull();
        message.ToolCalls.Should().HaveCount(1);
        message.ToolCalls![0].Id.Should().Be("call_abc123");
        message.ToolCalls[0].Function.Name.Should().Be("read_file");
        message.ToolCalls[0].Function.Arguments.Should().Contain("src/main.cs");
    }

    [Fact]
    public void ChatCompletionResponse_ShouldDeserialize_StreamingDelta()
    {
        var json = """
            {
                "choices": [
                    {
                        "index": 0,
                        "delta": {
                            "role": "assistant",
                            "content": "Hello"
                        },
                        "finish_reason": null
                    }
                ]
            }
            """;

        var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json);

        response.Should().NotBeNull();
        response!.Choices[0].Delta!.Content.Should().Be("Hello");
        response.Choices[0].FinishReason.Should().BeNull();
    }

    [Theory]
    [InlineData("system", "You are helpful")]
    [InlineData("user", "Hello")]
    [InlineData("assistant", "Hi there")]
    public void ChatCompletionMessage_ShouldSerializeRoles(string role, string content)
    {
        var message = new ChatCompletionMessage { Role = role, Content = content };

        var json = JsonSerializer.Serialize(message);

        json.Should().Contain($"\"role\":\"{role}\"");
        json.Should().Contain($"\"content\":\"{content}\"");
    }

    [Fact]
    public void ChatCompletionMessage_FactoryMethods_ShouldCreateCorrectRoles()
    {
        var system = ChatCompletionMessage.System("Be helpful");
        var user = ChatCompletionMessage.User("Hello");
        var assistant = ChatCompletionMessage.Assistant("Hi");
        var tool = ChatCompletionMessage.ToolResult("call_123", "file contents");

        system.Role.Should().Be("system");
        system.Content.Should().Be("Be helpful");

        user.Role.Should().Be("user");
        user.Content.Should().Be("Hello");

        assistant.Role.Should().Be("assistant");
        assistant.Content.Should().Be("Hi");

        tool.Role.Should().Be("tool");
        tool.ToolCallId.Should().Be("call_123");
        tool.Content.Should().Be("file contents");
    }

    [Fact]
    public void ChatCompletionMessage_ShouldOmitNullProperties()
    {
        var message = ChatCompletionMessage.User("Hello");

        var json = JsonSerializer.Serialize(message);

        json.Should().NotContain("\"tool_calls\"");
        json.Should().NotContain("\"tool_call_id\"");
        json.Should().NotContain("\"name\"");
        json.Should().NotContain("\"reasoning_content\"");
    }

    [Fact]
    public void ToolDefinition_ShouldSerialize_WithSchema()
    {
        var parameters = JsonSerializer.Deserialize<JsonElement>("""
            {
                "type": "object",
                "properties": {
                    "path": { "type": "string" },
                    "line_start": { "type": "integer" }
                },
                "required": ["path"]
            }
            """);

        var tool = new ToolDefinition
        {
            Function = new FunctionDefinition
            {
                Name = "read_file",
                Description = "Read a file from the workspace",
                Parameters = parameters
            }
        };

        var json = JsonSerializer.Serialize(tool);

        json.Should().Contain("\"type\":\"function\"");
        json.Should().Contain("\"name\":\"read_file\"");
        json.Should().Contain("\"description\":\"Read a file from the workspace\"");
        json.Should().Contain("\"properties\"");
    }

    [Fact]
    public void ToolCall_ShouldRoundTrip()
    {
        var toolCall = new ToolCall
        {
            Id = "call_abc",
            Type = "function",
            Function = new ToolCallFunction
            {
                Name = "write_file",
                Arguments = """{"path":"test.cs","content":"// hello"}"""
            }
        };

        var json = JsonSerializer.Serialize(toolCall);
        var deserialized = JsonSerializer.Deserialize<ToolCall>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be("call_abc");
        deserialized.Function.Name.Should().Be("write_file");
        deserialized.Function.Arguments.Should().Contain("test.cs");
    }

    [Fact]
    public void LlmModelConfig_GetCompletionsUrl_ShouldBuildCorrectUrl()
    {
        var config = new LlmModelConfig
        {
            HostUrl = "https://openrouter.ai/api",
            CompletionsPath = "v1/chat/completions"
        };

        config.GetCompletionsUrl().Should().Be("https://openrouter.ai/api/v1/chat/completions");
    }

    [Fact]
    public void LlmModelConfig_GetCompletionsUrl_ShouldHandleTrailingSlash()
    {
        var config = new LlmModelConfig
        {
            HostUrl = "http://127.0.0.1:1234/",
            CompletionsPath = "/v1/chat/completions"
        };

        config.GetCompletionsUrl().Should().Be("http://127.0.0.1:1234/v1/chat/completions");
    }

    [Fact]
    public void LlmModelConfig_ShouldHaveSensibleDefaults()
    {
        var config = new LlmModelConfig();

        config.HostUrl.Should().Be("http://127.0.0.1:1234");
        config.CompletionsPath.Should().Be("v1/chat/completions");
        config.AuthScheme.Should().Be("Bearer");
        config.MaxContextLength.Should().Be(32_000);
        config.MaxOutputTokens.Should().Be(4_096);
        config.Temperature.Should().Be(0.2);
        config.SupportsTools.Should().BeTrue();
        config.IsThinkingModel.Should().BeFalse();
        config.HttpTimeoutSeconds.Should().Be(120);
        config.Enabled.Should().BeTrue();
    }

    [Fact]
    public void ChatCompletionResponse_ShouldDeserialize_WithReasoningContent()
    {
        var json = """
            {
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": "The answer is 42.",
                            "reasoning_content": "Let me think step by step..."
                        },
                        "finish_reason": "stop"
                    }
                ]
            }
            """;

        var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json);

        response.Should().NotBeNull();
        var message = response!.Choices[0].Message!;
        message.Content.Should().Be("The answer is 42.");
        message.ReasoningContent.Should().Be("Let me think step by step...");
    }

    [Fact]
    public void FullRequest_ShouldRoundTrip()
    {
        var request = new ChatCompletionRequest
        {
            Model = "deepseek-chat",
            Messages =
            [
                ChatCompletionMessage.System("You are a coding assistant."),
                ChatCompletionMessage.User("Write a hello world"),
                ChatCompletionMessage.Assistant("Here is the code"),
            ],
            Temperature = 0.2,
            MaxTokens = 4096,
            Stream = false
        };

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<ChatCompletionRequest>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Model.Should().Be("deepseek-chat");
        deserialized.Messages.Should().HaveCount(3);
        deserialized.Messages[0].Role.Should().Be("system");
        deserialized.Messages[1].Role.Should().Be("user");
        deserialized.Messages[2].Role.Should().Be("assistant");
        deserialized.Temperature.Should().Be(0.2);
        deserialized.MaxTokens.Should().Be(4096);
        deserialized.Stream.Should().BeFalse();
    }
}
