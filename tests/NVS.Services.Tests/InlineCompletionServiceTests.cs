using NVS.Core.Interfaces;
using NVS.Core.LLM;
using NVS.Core.Models.Settings;
using NVS.Services.LLM;

namespace NVS.Services.Tests;

public class InlineCompletionServiceTests
{
    private static LlmResponse MakeResponse(string content) => new()
    {
        Content = content,
        InputTokens = 10,
        OutputTokens = 5,
        Model = "test-model"
    };

    [Fact]
    public void BuildCompletionPrompt_ContainsLanguageAndFile()
    {
        var prompt = InlineCompletionService.BuildCompletionPrompt(
            "var x = ", "", "CSharp", "Program.cs");

        prompt.Should().Contain("CSharp");
        prompt.Should().Contain("Program.cs");
        prompt.Should().Contain("var x = ");
    }

    [Fact]
    public void BuildCompletionPrompt_IncludesSuffixContext()
    {
        var prompt = InlineCompletionService.BuildCompletionPrompt(
            "var x = ", "Console.WriteLine(x);", "CSharp", "Program.cs");

        prompt.Should().Contain("Console.WriteLine(x);");
    }

    [Fact]
    public void BuildCompletionPrompt_TruncatesLongPrefix()
    {
        var longPrefix = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"line {i}"));

        var prompt = InlineCompletionService.BuildCompletionPrompt(
            longPrefix, "", "CSharp", "Test.cs");

        prompt.Should().Contain("line 50");
        prompt.Should().Contain("line 21");
        prompt.Should().NotContain("line 1\n");
    }

    [Fact]
    public void BuildCompletionPrompt_TruncatesLongSuffix()
    {
        var longSuffix = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"after {i}"));

        var prompt = InlineCompletionService.BuildCompletionPrompt(
            "var x = ", longSuffix, "CSharp", "Test.cs");

        prompt.Should().Contain("after 10");
        prompt.Should().NotContain("after 11");
    }

    [Fact]
    public void CleanCompletion_RemovesMarkdownFences()
    {
        var raw = "```csharp\nConsole.WriteLine();\n```";

        var result = InlineCompletionService.CleanCompletion(raw, "");

        result.Should().Be("Console.WriteLine();");
    }

    [Fact]
    public void CleanCompletion_LimitsToThreeLines()
    {
        var raw = "line1\nline2\nline3\nline4\nline5";

        var result = InlineCompletionService.CleanCompletion(raw, "");

        result.Split('\n').Should().HaveCount(3);
        result.Should().NotContain("line4");
    }

    [Fact]
    public void CleanCompletion_ReturnsEmptyForNullOrEmpty()
    {
        InlineCompletionService.CleanCompletion("", "").Should().BeEmpty();
        InlineCompletionService.CleanCompletion(null!, "").Should().BeEmpty();
    }

    [Fact]
    public void CleanCompletion_TrimsWhitespace()
    {
        var raw = "  Console.WriteLine();  ";

        var result = InlineCompletionService.CleanCompletion(raw, "");

        result.Should().Be("Console.WriteLine();");
    }

    [Fact]
    public async Task GetInlineCompletionAsync_ReturnsNull_WhenNotConfigured()
    {
        var llmService = Substitute.For<ILlmService>();
        llmService.IsConfigured.Returns(false);
        var settings = new LlmSettings { EnableAutoComplete = true };
        var service = new InlineCompletionService(llmService, () => settings);

        var result = await service.GetInlineCompletionAsync(
            "test.cs", 1, 1, "var x = ", "", "CSharp");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInlineCompletionAsync_ReturnsNull_WhenAutoCompleteDisabled()
    {
        var llmService = Substitute.For<ILlmService>();
        llmService.IsConfigured.Returns(true);
        var settings = new LlmSettings { EnableAutoComplete = false };
        var service = new InlineCompletionService(llmService, () => settings);

        var result = await service.GetInlineCompletionAsync(
            "test.cs", 1, 1, "var x = ", "", "CSharp");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInlineCompletionAsync_ReturnsCompletion_WhenConfigured()
    {
        var llmService = Substitute.For<ILlmService>();
        llmService.IsConfigured.Returns(true);
        llmService.SendAsync(Arg.Any<ChatCompletionRequest>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns(MakeResponse("Console.WriteLine();"));
        var settings = new LlmSettings { EnableAutoComplete = true, Model = "test-model" };
        var service = new InlineCompletionService(llmService, () => settings);

        var result = await service.GetInlineCompletionAsync(
            "test.cs", 1, 10, "var x = ", "", "CSharp");

        result.Should().Be("Console.WriteLine();");
    }

    [Fact]
    public async Task GetInlineCompletionAsync_ReturnsNull_OnCancellation()
    {
        var llmService = Substitute.For<ILlmService>();
        llmService.IsConfigured.Returns(true);
        llmService.SendAsync(Arg.Any<ChatCompletionRequest>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns<LlmResponse>(_ => throw new OperationCanceledException());
        var settings = new LlmSettings { EnableAutoComplete = true, Model = "test-model" };
        var service = new InlineCompletionService(llmService, () => settings);

        var result = await service.GetInlineCompletionAsync(
            "test.cs", 1, 1, "", "", "CSharp");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInlineCompletionAsync_ReturnsNull_OnException()
    {
        var llmService = Substitute.For<ILlmService>();
        llmService.IsConfigured.Returns(true);
        llmService.SendAsync(Arg.Any<ChatCompletionRequest>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns<LlmResponse>(_ => throw new HttpRequestException("connection failed"));
        var settings = new LlmSettings { EnableAutoComplete = true, Model = "test-model" };
        var service = new InlineCompletionService(llmService, () => settings);

        var result = await service.GetInlineCompletionAsync(
            "test.cs", 1, 1, "", "", "CSharp");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInlineCompletionAsync_ReturnsNull_WhenResponseIsWhitespace()
    {
        var llmService = Substitute.For<ILlmService>();
        llmService.IsConfigured.Returns(true);
        llmService.SendAsync(Arg.Any<ChatCompletionRequest>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns(MakeResponse("   \n  "));
        var settings = new LlmSettings { EnableAutoComplete = true, Model = "test-model" };
        var service = new InlineCompletionService(llmService, () => settings);

        var result = await service.GetInlineCompletionAsync(
            "test.cs", 1, 1, "", "", "CSharp");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetInlineCompletionAsync_SetsStreamFalse()
    {
        var llmService = Substitute.For<ILlmService>();
        llmService.IsConfigured.Returns(true);
        ChatCompletionRequest? capturedRequest = null;
        llmService.SendAsync(Arg.Do<ChatCompletionRequest>(r => capturedRequest = r), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns(MakeResponse("x"));
        var settings = new LlmSettings { EnableAutoComplete = true, Model = "my-model" };
        var service = new InlineCompletionService(llmService, () => settings);

        await service.GetInlineCompletionAsync("test.cs", 1, 1, "a", "b", "CSharp");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Stream.Should().BeFalse();
        capturedRequest.Model.Should().Be("my-model");
        capturedRequest.Temperature.Should().Be(0.0);
    }
}
