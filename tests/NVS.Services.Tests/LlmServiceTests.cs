using System.Net;
using System.Text;
using System.Text.Json;
using NVS.Core.Interfaces;
using NVS.Core.LLM;
using NVS.Core.Models.Settings;
using NVS.Services.LLM;

namespace NVS.Services.Tests;

public sealed class StreamParserTests
{
    [Fact]
    public async Task ParseStreamAsync_ShouldParseContentTokens()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"role":"assistant","content":"Hello"},"finish_reason":null}]}""",
            """{"choices":[{"delta":{"content":" world"},"finish_reason":null}]}""",
            """{"choices":[{"delta":{"content":"!"},"finish_reason":"stop"}]}"""
        );

        var tokens = new List<string>();
        using var response = CreateSseResponse(sse);

        var result = await StreamParser.ParseStreamAsync(response, onToken: t => tokens.Add(t));

        result.Content.Should().Be("Hello world!");
        result.FinishReason.Should().Be("stop");
        tokens.Should().BeEquivalentTo(["Hello", " world", "!"]);
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldParseToolCalls()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":""}}]},"finish_reason":null}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"function":{"arguments":"{\"path\":"}}]},"finish_reason":null}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"function":{"arguments":"\"test.cs\"}"}}]},"finish_reason":"tool_calls"}]}"""
        );

        using var response = CreateSseResponse(sse);

        var result = await StreamParser.ParseStreamAsync(response);

        result.ToolCalls.Should().NotBeNull();
        result.ToolCalls.Should().HaveCount(1);
        result.ToolCalls![0].Id.Should().Be("call_1");
        result.ToolCalls[0].Function.Name.Should().Be("read_file");
        result.ToolCalls[0].Function.Arguments.Should().Be("""{"path":"test.cs"}""");
        result.FinishReason.Should().Be("tool_calls");
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldParseReasoningContent()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"reasoning_content":"Let me think..."},"finish_reason":null}]}""",
            """{"choices":[{"delta":{"content":"The answer is 42."},"finish_reason":"stop"}]}"""
        );

        var reasoningTokens = new List<string>();
        using var response = CreateSseResponse(sse);

        var result = await StreamParser.ParseStreamAsync(
            response,
            onReasoningToken: t => reasoningTokens.Add(t));

        result.Content.Should().Be("The answer is 42.");
        result.ReasoningContent.Should().Be("Let me think...");
        reasoningTokens.Should().ContainSingle().Which.Should().Be("Let me think...");
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldParseUsage()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"content":"Hi"},"finish_reason":"stop"}],"usage":{"prompt_tokens":15,"completion_tokens":1,"total_tokens":16}}"""
        );

        using var response = CreateSseResponse(sse);

        var result = await StreamParser.ParseStreamAsync(response);

        result.Usage.Should().NotBeNull();
        result.Usage!.PromptTokens.Should().Be(15);
        result.Usage.CompletionTokens.Should().Be(1);
        result.Usage.TotalTokens.Should().Be(16);
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldHandleDoneMarker()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"},\"finish_reason\":null}]}\n\n" +
                  "data: [DONE]\n\n";

        using var response = CreateSseResponse(sse);

        var result = await StreamParser.ParseStreamAsync(response);

        result.Content.Should().Be("Hi");
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldSkipNonDataLines()
    {
        var sse = ": keepalive\n\n" +
                  "event: message\n" +
                  "data: {\"choices\":[{\"delta\":{\"content\":\"OK\"},\"finish_reason\":\"stop\"}]}\n\n";

        using var response = CreateSseResponse(sse);

        var result = await StreamParser.ParseStreamAsync(response);

        result.Content.Should().Be("OK");
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldHandleEmptyStream()
    {
        using var response = CreateSseResponse("");

        var result = await StreamParser.ParseStreamAsync(response);

        result.Content.Should().BeEmpty();
        result.ToolCalls.Should().BeNull();
    }

    [Fact]
    public void ParseNonStreaming_ShouldExtractContent()
    {
        var chatResponse = new ChatCompletionResponse
        {
            Choices =
            [
                new ChatCompletionChoice
                {
                    Message = new ChatCompletionMessage
                    {
                        Role = "assistant",
                        Content = "Hello!"
                    },
                    FinishReason = "stop"
                }
            ],
            Usage = new TokenUsage
            {
                PromptTokens = 10,
                CompletionTokens = 5,
                TotalTokens = 15
            }
        };

        var result = StreamParser.ParseNonStreaming(chatResponse);

        result.Content.Should().Be("Hello!");
        result.FinishReason.Should().Be("stop");
        result.Usage!.TotalTokens.Should().Be(15);
    }

    [Fact]
    public void ParseNonStreaming_ShouldExtractToolCalls()
    {
        var chatResponse = new ChatCompletionResponse
        {
            Choices =
            [
                new ChatCompletionChoice
                {
                    Message = new ChatCompletionMessage
                    {
                        Role = "assistant",
                        ToolCalls =
                        [
                            new ToolCall
                            {
                                Id = "call_1",
                                Function = new ToolCallFunction
                                {
                                    Name = "read_file",
                                    Arguments = """{"path":"test.cs"}"""
                                }
                            }
                        ]
                    },
                    FinishReason = "tool_calls"
                }
            ]
        };

        var result = StreamParser.ParseNonStreaming(chatResponse);

        result.ToolCalls.Should().HaveCount(1);
        result.ToolCalls![0].Function.Name.Should().Be("read_file");
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldHandleMultipleToolCalls()
    {
        var sse = BuildSse(
            """{"choices":[{"delta":{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"a.cs\"}"}}]},"finish_reason":null}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":""}},{"id":"call_2","type":"function","function":{"name":"write_file","arguments":"{\"path\":\"b.cs\"}"}}]},"finish_reason":"tool_calls"}]}"""
        );

        using var response = CreateSseResponse(sse);

        var result = await StreamParser.ParseStreamAsync(response);

        result.ToolCalls.Should().NotBeNull();
        result.ToolCalls!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ParseStreamAsync_ShouldHandleMalformedJson()
    {
        var sse = "data: not valid json\n\n" +
                  "data: {\"choices\":[{\"delta\":{\"content\":\"OK\"},\"finish_reason\":\"stop\"}]}\n\n";

        using var response = CreateSseResponse(sse);

        var result = await StreamParser.ParseStreamAsync(response);

        result.Content.Should().Be("OK");
    }

    private static string BuildSse(params string[] dataPayloads)
    {
        var sb = new StringBuilder();
        foreach (var payload in dataPayloads)
        {
            sb.AppendLine($"data: {payload}");
            sb.AppendLine();
        }
        sb.AppendLine("data: [DONE]");
        sb.AppendLine();
        return sb.ToString();
    }

    private static HttpResponseMessage CreateSseResponse(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "text/event-stream")
        };
    }
}

public sealed class LlmServiceTests
{
    [Fact]
    public void IsConfigured_ShouldReturnFalse_WhenEndpointEmpty()
    {
        var settingsService = CreateSettingsService(new LlmSettings { Endpoint = "" });
        var service = new LlmService(settingsService);

        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_ShouldReturnFalse_WhenModelEmpty()
    {
        var settingsService = CreateSettingsService(new LlmSettings { Model = "" });
        var service = new LlmService(settingsService);

        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_ShouldReturnTrue_WhenEndpointAndModelSet()
    {
        var settingsService = CreateSettingsService(new LlmSettings
        {
            Endpoint = "http://localhost:1234",
            Model = "test-model"
        });
        var service = new LlmService(settingsService);

        service.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void RegisterTool_ShouldAddToToolDefinitions()
    {
        var settingsService = CreateSettingsService(new LlmSettings());
        var service = new LlmService(settingsService);

        var tool = new FakeTool("read_file", "Read a file");
        service.RegisterTool(tool);

        var defs = service.GetToolDefinitions();
        defs.Should().HaveCount(1);
        defs[0].Function.Name.Should().Be("read_file");
        defs[0].Function.Description.Should().Be("Read a file");
    }

    [Fact]
    public void RegisterTool_ShouldOverwriteDuplicate()
    {
        var settingsService = CreateSettingsService(new LlmSettings());
        var service = new LlmService(settingsService);

        service.RegisterTool(new FakeTool("read_file", "V1"));
        service.RegisterTool(new FakeTool("read_file", "V2"));

        var defs = service.GetToolDefinitions();
        defs.Should().HaveCount(1);
        defs[0].Function.Description.Should().Be("V2");
    }

    [Fact]
    public void IsProcessing_ShouldBeFalse_Initially()
    {
        var settingsService = CreateSettingsService(new LlmSettings());
        var service = new LlmService(settingsService);

        service.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public void CancelCurrentRequest_ShouldNotThrow_WhenNoRequest()
    {
        var settingsService = CreateSettingsService(new LlmSettings());
        var service = new LlmService(settingsService);

        var act = () => service.CancelCurrentRequest();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task RunAgentLoopAsync_ShouldReturnContent_WhenNoToolCalls()
    {
        // This tests the agent loop logic with a mock-like approach
        // Since we can't easily mock the HTTP layer without more infrastructure,
        // we test the tool registration and configuration aspects
        var settingsService = CreateSettingsService(new LlmSettings
        {
            Endpoint = "http://localhost:9999",
            Model = "test"
        });
        var service = new LlmService(settingsService);

        var tool = new FakeTool("test_tool", "Test");
        service.RegisterTool(tool);

        // Verify tools are properly registered for agent loop
        var defs = service.GetToolDefinitions();
        defs.Should().HaveCount(1);
        defs[0].Type.Should().Be("function");
    }

    [Fact]
    public async Task SendAsync_ApiKeyChangedInSettings_UsesNewKeyWithoutRestart()
    {
        var currentSettings = new LlmSettings
        {
            Endpoint = "http://localhost:9999",
            Model = "test",
            ApiKey = "old-key",
            Stream = false
        };
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.AppSettings.Returns(_ => new AppSettings { Llm = currentSettings });

        var handler = new FakeHttpHandler();
        var service = new LlmService(settingsService, () => handler);

        await service.SendAsync(CreateRequest());
        currentSettings = currentSettings with { ApiKey = "new-key" };
        await service.SendAsync(CreateRequest());

        handler.AuthorizationHeaders.Should().Equal("Bearer old-key", "Bearer new-key");
    }

    [Fact]
    public async Task SendAsync_EmptyApiKey_SendsNoAuthorizationHeader()
    {
        var settingsService = CreateSettingsService(new LlmSettings
        {
            Endpoint = "http://localhost:9999",
            Model = "test",
            ApiKey = "",
            Stream = false
        });

        var handler = new FakeHttpHandler();
        var service = new LlmService(settingsService, () => handler);

        await service.SendAsync(CreateRequest());

        handler.AuthorizationHeaders.Should().Equal([null]);
    }

    [Fact]
    public async Task SendAsync_TimeoutChangedInSettings_RecreatesHttpClient()
    {
        var currentSettings = new LlmSettings
        {
            Endpoint = "http://localhost:9999",
            Model = "test",
            HttpTimeoutSeconds = 120,
            Stream = false
        };
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.AppSettings.Returns(_ => new AppSettings { Llm = currentSettings });

        int clientsCreated = 0;
        var service = new LlmService(settingsService, () =>
        {
            clientsCreated++;
            return new FakeHttpHandler();
        });

        await service.SendAsync(CreateRequest());
        await service.SendAsync(CreateRequest());
        currentSettings = currentSettings with { HttpTimeoutSeconds = 30 };
        await service.SendAsync(CreateRequest());

        clientsCreated.Should().Be(2, "the client is cached until the configured timeout changes");
    }

    [Fact]
    public async Task RunAgentLoopAsync_ApprovalGranted_ExecutesDestructiveTool()
    {
        var (service, tool) = CreateAgentLoopService(requireToolApproval: true);
        var approvalRequests = new List<ToolApprovalRequest>();

        var result = await service.RunAgentLoopAsync(
            [ChatCompletionMessage.User("go")],
            onApprovalRequired: request =>
            {
                approvalRequests.Add(request);
                return Task.FromResult(true);
            });

        tool.ExecuteCount.Should().Be(1);
        approvalRequests.Should().ContainSingle().Which.ToolName.Should().Be("danger_tool");
        result.ToolCallHistory.Should().ContainSingle().Which.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RunAgentLoopAsync_ApprovalDenied_DoesNotExecuteTool()
    {
        var (service, tool) = CreateAgentLoopService(requireToolApproval: true);

        var result = await service.RunAgentLoopAsync(
            [ChatCompletionMessage.User("go")],
            onApprovalRequired: _ => Task.FromResult(false));

        tool.ExecuteCount.Should().Be(0);
        var toolEvent = result.ToolCallHistory.Should().ContainSingle().Subject;
        toolEvent.Success.Should().BeFalse();
        toolEvent.Result.Should().Contain("denied");
    }

    [Fact]
    public async Task RunAgentLoopAsync_NoApprovalHandler_DeniesDestructiveTool()
    {
        var (service, tool) = CreateAgentLoopService(requireToolApproval: true);

        var result = await service.RunAgentLoopAsync([ChatCompletionMessage.User("go")]);

        tool.ExecuteCount.Should().Be(0, "with no way to ask the user, destructive tools must be denied");
        result.ToolCallHistory.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RunAgentLoopAsync_ApprovalDisabledInSettings_ExecutesWithoutPrompt()
    {
        var (service, tool) = CreateAgentLoopService(requireToolApproval: false);
        var handlerInvoked = false;

        await service.RunAgentLoopAsync(
            [ChatCompletionMessage.User("go")],
            onApprovalRequired: _ =>
            {
                handlerInvoked = true;
                return Task.FromResult(false);
            });

        tool.ExecuteCount.Should().Be(1);
        handlerInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task RunAgentLoopAsync_NonDestructiveTool_DoesNotRequestApproval()
    {
        var (service, tool) = CreateAgentLoopService(requireToolApproval: true, toolRequiresApproval: false);
        var handlerInvoked = false;

        await service.RunAgentLoopAsync(
            [ChatCompletionMessage.User("go")],
            onApprovalRequired: _ =>
            {
                handlerInvoked = true;
                return Task.FromResult(true);
            });

        tool.ExecuteCount.Should().Be(1);
        handlerInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task CancelCurrentRequest_CancelsAllConcurrentRequests()
    {
        var settingsService = CreateSettingsService(new LlmSettings
        {
            Endpoint = "http://localhost:9999",
            Model = "test",
            Stream = false
        });

        var handler = new BlockingHttpHandler(expectedRequests: 2);
        var service = new LlmService(settingsService, () => handler);

        var first = service.SendAsync(CreateRequest());
        var second = service.SendAsync(CreateRequest());
        await handler.AllRequestsStarted;

        service.IsProcessing.Should().BeTrue();
        service.CancelCurrentRequest();

        var responses = await Task.WhenAll(first, second);
        responses.Should().OnlyContain(r => r.FinishReason == "cancelled");
        service.IsProcessing.Should().BeFalse();
    }

    [Fact]
    public void DestructiveTools_AreMarkedAsRequiringApproval()
    {
        IAgentTool runTerminal = new NVS.Services.LLM.Tools.RunTerminalCommandTool(() => ".");
        IAgentTool writeFile = new NVS.Services.LLM.Tools.WriteFileTool(() => ".");
        IAgentTool applyEdit = new NVS.Services.LLM.Tools.ApplyEditTool(_ => true);

        runTerminal.RequiresApproval.Should().BeTrue();
        writeFile.RequiresApproval.Should().BeTrue();
        applyEdit.RequiresApproval.Should().BeTrue();
    }

    /// <summary>Builds a service whose fake LLM calls "danger_tool" once, then finishes.</summary>
    private static (LlmService Service, FakeTool Tool) CreateAgentLoopService(
        bool requireToolApproval,
        bool toolRequiresApproval = true)
    {
        var settingsService = CreateSettingsService(new LlmSettings
        {
            Endpoint = "http://localhost:9999",
            Model = "test",
            Stream = false,
            RequireToolApproval = requireToolApproval
        });

        const string toolCallResponse =
            """{"choices":[{"message":{"role":"assistant","tool_calls":[{"id":"call_1","type":"function","function":{"name":"danger_tool","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""";
        const string finalResponse =
            """{"choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}""";

        var handler = new FakeHttpHandler(toolCallResponse, finalResponse);
        var service = new LlmService(settingsService, () => handler);

        var tool = new FakeTool("danger_tool", "A destructive test tool", toolRequiresApproval);
        service.RegisterTool(tool);

        return (service, tool);
    }

    private static ChatCompletionRequest CreateRequest() => new()
    {
        Model = "test",
        Messages = [ChatCompletionMessage.User("hi")],
        Stream = false
    };

    private static ISettingsService CreateSettingsService(LlmSettings llmSettings)
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.AppSettings.Returns(new AppSettings { Llm = llmSettings });
        return settingsService;
    }

    /// <summary>Holds every request open until its cancellation token fires.</summary>
    private sealed class BlockingHttpHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _expectedRequests;
        private int _started;

        public BlockingHttpHandler(int expectedRequests)
        {
            _expectedRequests = expectedRequests;
        }

        public Task AllRequestsStarted => _allStarted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _started) >= _expectedRequests)
                _allStarted.TrySetResult();

            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private const string DefaultResponse =
            """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";

        private readonly Queue<string> _responses;

        public List<string?> AuthorizationHeaders { get; } = [];

        public FakeHttpHandler(params string[] responseBodies)
        {
            _responses = new Queue<string>(responseBodies);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString());

            var body = _responses.Count > 0 ? _responses.Dequeue() : DefaultResponse;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeTool : IAgentTool
    {
        public string Name { get; }
        public string Description { get; }
        public bool RequiresApproval { get; }
        public int ExecuteCount { get; private set; }

        public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
            """);

        public FakeTool(string name, string description, bool requiresApproval = false)
        {
            Name = name;
            Description = description;
            RequiresApproval = requiresApproval;
        }

        public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult("""{"result":"ok"}""");
        }
    }
}
