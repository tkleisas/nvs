using FluentAssertions;
using NVS.Services.LLM;

namespace NVS.Services.Tests;

public sealed class PromptContextTests
{
    [Fact]
    public void BuildPrompt_WithNullContext_ReturnsBasePromptOnly()
    {
        var result = PromptLoader.BuildPrompt("general", null);

        result.Should().NotBeNullOrEmpty();
        result.Should().NotContain("## Current Context");
    }

    [Fact]
    public void BuildPrompt_WithWorkspacePath_IncludesWorkspace()
    {
        var ctx = new PromptContext { WorkspacePath = "/projects/myapp" };

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().Contain("Workspace: /projects/myapp");
    }

    [Fact]
    public void BuildPrompt_WithGitBranch_IncludesBranchInfo()
    {
        var ctx = new PromptContext { GitBranch = "feature/login" };

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().Contain("Git branch: feature/login");
    }

    [Fact]
    public void BuildPrompt_WithGitStatus_IncludesStatusSummary()
    {
        var ctx = new PromptContext { GitStatusSummary = "3 changed, 1 staged" };

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().Contain("Git status: 3 changed, 1 staged");
    }

    [Fact]
    public void BuildPrompt_WithOpenFiles_IncludesFileList()
    {
        var ctx = new PromptContext { OpenFiles = ["Program.cs", "Startup.cs", "appsettings.json"] };

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().Contain("Open files: Program.cs, Startup.cs, appsettings.json");
    }

    [Fact]
    public void BuildPrompt_WithDiagnostics_IncludesDiagnosticsList()
    {
        var ctx = new PromptContext
        {
            Diagnostics = ["[Error] CS0103: The name 'foo' does not exist", "[Warning] CS0168: Variable unused"]
        };

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().Contain("Current diagnostics:");
        result.Should().Contain("CS0103");
        result.Should().Contain("CS0168");
    }

    [Fact]
    public void BuildPrompt_WithSelectedText_IncludesTextInCodeBlock()
    {
        var ctx = new PromptContext { SelectedText = "var x = 42;" };

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().Contain("Selected text:");
        result.Should().Contain("var x = 42;");
    }

    [Fact]
    public void BuildPrompt_WithSolutionName_IncludesName()
    {
        var ctx = new PromptContext { SolutionName = "MyApp, MyLib" };

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().Contain("Solution: MyApp, MyLib");
    }

    [Fact]
    public void BuildPrompt_WithAttachedFiles_IncludesFileContents()
    {
        var ctx = new PromptContext
        {
            AttachedFiles = ["### readme.md\n```\n# Hello\n```"]
        };

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().Contain("Attached file contents:");
        result.Should().Contain("### readme.md");
    }

    [Fact]
    public void BuildPrompt_WithAllFields_IncludesAllSections()
    {
        var ctx = new PromptContext
        {
            WorkspacePath = "/myapp",
            ActiveFilePath = "src/main.cs",
            ActiveFileLanguage = "CSharp",
            SolutionName = "MyApp",
            GitBranch = "main",
            GitStatusSummary = "2 changed, 0 staged",
            OpenFiles = ["main.cs"],
            Diagnostics = ["[Error] test error"],
            AttachedFiles = ["### file.txt\n```\ncontent\n```"]
        };

        var result = PromptLoader.BuildPrompt("coding", ctx);

        result.Should().Contain("## Current Context");
        result.Should().Contain("Workspace: /myapp");
        result.Should().Contain("Active file: src/main.cs");
        result.Should().Contain("Language: CSharp");
        result.Should().Contain("Solution: MyApp");
        result.Should().Contain("Git branch: main");
        result.Should().Contain("Git status: 2 changed, 0 staged");
        result.Should().Contain("Open files: main.cs");
        result.Should().Contain("Current diagnostics:");
        result.Should().Contain("Attached file contents:");
    }

    [Fact]
    public void BuildPrompt_DiagnosticsLimitedToTen()
    {
        var diags = Enumerable.Range(1, 15).Select(i => $"[Error] E{i:D3}: error {i}").ToList();
        var ctx = new PromptContext { Diagnostics = diags };

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().Contain("E010");
        result.Should().NotContain("E011");
    }

    [Fact]
    public void BuildPrompt_EmptyContext_ReturnsBasePromptOnly()
    {
        var ctx = new PromptContext();

        var result = PromptLoader.BuildPrompt("general", ctx);

        result.Should().NotContain("## Current Context");
    }
}
