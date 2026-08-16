using NVS.Core.Interfaces;
using NVS.Core.LLM;
using NVS.Helpers;

namespace NVS.Tests;

public class LlmAdapterTests
{
    private static ILlmService ConfiguredInner(string reply)
    {
        var inner = Substitute.For<ILlmService>();
        inner.IsConfigured.Returns(true);
        inner.SendAsync(
                Arg.Any<ChatCompletionRequest>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>())
            .Returns(new LlmResponse
            {
                Content = reply,
                InputTokens = 1,
                OutputTokens = 1,
                Model = "test-model",
            });
        return inner;
    }

    private static ChatCompletionRequest? CapturedRequest(ILlmService inner) =>
        inner.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(ILlmService.SendAsync))
            .GetArguments()[0] as ChatCompletionRequest;

    [Fact]
    public async Task ApiClientAdapter_ChatAsync_MapsMessagesAndReturnsContent()
    {
        var inner = ConfiguredInner("analysis text");
        var adapter = new ApiClientLlmAdapter(inner);

        var result = await adapter.ChatAsync("system prompt", "user prompt");

        result.Should().Be("analysis text");
        var request = CapturedRequest(inner);
        request.Should().NotBeNull();
        request!.Stream.Should().BeFalse();
        request.Messages.Should().HaveCount(2);
        request.Messages[0].Role.Should().Be("system");
        request.Messages[0].Content.Should().Be("system prompt");
        request.Messages[1].Role.Should().Be("user");
        request.Messages[1].Content.Should().Be("user prompt");
    }

    [Fact]
    public async Task SqlExplorerAdapter_ChatAsync_MapsMessagesAndReturnsContent()
    {
        var inner = ConfiguredInner("SELECT 1;");
        var adapter = new SqlExplorerLlmAdapter(inner);

        var result = await adapter.ChatAsync("system prompt", "user prompt");

        result.Should().Be("SELECT 1;");
        var request = CapturedRequest(inner);
        request.Should().NotBeNull();
        request!.Stream.Should().BeFalse();
        request.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task SqlExplorerAdapter_ChatStreamingAsync_StreamsTokensAndRequestsStreaming()
    {
        var inner = Substitute.For<ILlmService>();
        inner.IsConfigured.Returns(true);
        inner.SendAsync(
                Arg.Any<ChatCompletionRequest>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<Action<string>?>())
            .Returns(async ci =>
            {
                var onToken = ci.ArgAt<Action<string>?>(1);
                onToken?.Invoke("SELECT ");
                onToken?.Invoke("1;");
                return new LlmResponse
                {
                    Content = "SELECT 1;",
                    InputTokens = 1,
                    OutputTokens = 1,
                    Model = "test-model",
                };
            });

        var adapter = new SqlExplorerLlmAdapter(inner);
        var tokens = new List<string>();

        var result = await adapter.ChatStreamingAsync("s", "u", tokens.Add);

        result.Should().Be("SELECT 1;");
        tokens.Should().Equal("SELECT ", "1;");

        var request = CapturedRequest(inner);
        request.Should().NotBeNull();
        request!.Stream.Should().BeTrue();
    }

    [Fact]
    public void Adapters_IsConfigured_PassesThrough()
    {
        var inner = ConfiguredInner("x");
        inner.IsConfigured.Returns(false);

        new ApiClientLlmAdapter(inner).IsConfigured.Should().BeFalse();
        new SqlExplorerLlmAdapter(inner).IsConfigured.Should().BeFalse();
    }
}
