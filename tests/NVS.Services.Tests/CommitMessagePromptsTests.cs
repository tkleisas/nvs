using NVS.Services.LLM;

namespace NVS.Services.Tests;

public class CommitMessagePromptsTests
{
    [Fact]
    public void Build_IncludesFilesAndPatch()
    {
        var (system, user) = CommitMessagePrompts.Build("diff --git a/x.cs", ["src/x.cs", "src/y.cs"]);

        system.Should().Contain("commit message");
        user.Should().Contain("src/x.cs");
        user.Should().Contain("diff --git a/x.cs");
    }

    [Fact]
    public void Clean_PlainText_PassesThrough()
    {
        CommitMessagePrompts.Clean("Add login validation").Should().Be("Add login validation");
    }

    [Fact]
    public void Clean_StripsMarkdownFence()
    {
        CommitMessagePrompts.Clean("```\nAdd login validation\n```").Should().Be("Add login validation");
        CommitMessagePrompts.Clean("```text\nAdd login validation\n```").Should().Be("Add login validation");
    }

    [Fact]
    public void Clean_StripsWrappingQuotes()
    {
        CommitMessagePrompts.Clean("\"Add login validation\"").Should().Be("Add login validation");
    }

    [Fact]
    public void Clean_Empty_ReturnsEmpty()
    {
        CommitMessagePrompts.Clean("   ").Should().Be(string.Empty);
    }
}
