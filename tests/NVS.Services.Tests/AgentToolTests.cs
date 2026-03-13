using System.Text.Json;
using NVS.Services.LLM.Tools;
using NVS.Services.LLM;

namespace NVS.Services.Tests;

public sealed class AgentToolTests : IDisposable
{
    private readonly string _testDir;

    public AgentToolTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"nvs_tool_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    // ---- ReadFileTool ----

    [Fact]
    public async Task ReadFile_ShouldReturnFileContents()
    {
        var filePath = Path.Combine(_testDir, "test.cs");
        await File.WriteAllTextAsync(filePath, "line1\nline2\nline3");

        var tool = new ReadFileTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"path":"test.cs"}""");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("path").GetString().Should().Be("test.cs");
        parsed.GetProperty("total_lines").GetInt32().Should().Be(3);
        parsed.GetProperty("content").GetString().Should().Contain("line1");
    }

    [Fact]
    public async Task ReadFile_WithLineRange_ShouldReturnSubset()
    {
        var filePath = Path.Combine(_testDir, "range.txt");
        await File.WriteAllTextAsync(filePath, "a\nb\nc\nd\ne");

        var tool = new ReadFileTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"path":"range.txt","line_start":2,"line_end":4}""");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("line_start").GetInt32().Should().Be(2);
        parsed.GetProperty("line_end").GetInt32().Should().Be(4);
        var content = parsed.GetProperty("content").GetString()!;
        content.Should().Contain("b");
        content.Should().Contain("c");
        content.Should().Contain("d");
        content.Should().NotContain("1. a");
    }

    [Fact]
    public async Task ReadFile_FileNotFound_ShouldReturnError()
    {
        var tool = new ReadFileTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"path":"nonexistent.txt"}""");

        result.Should().Contain("error");
        result.Should().Contain("not found");
    }

    // ---- WriteFileTool ----

    [Fact]
    public async Task WriteFile_ShouldCreateNewFile()
    {
        var tool = new WriteFileTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"path":"new.cs","content":"// hello world"}""");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("action").GetString().Should().Be("created");

        var content = await File.ReadAllTextAsync(Path.Combine(_testDir, "new.cs"));
        content.Should().Be("// hello world");
    }

    [Fact]
    public async Task WriteFile_ShouldOverwriteExisting()
    {
        var filePath = Path.Combine(_testDir, "existing.cs");
        await File.WriteAllTextAsync(filePath, "old");

        var tool = new WriteFileTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"path":"existing.cs","content":"new"}""");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("action").GetString().Should().Be("updated");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Be("new");
    }

    [Fact]
    public async Task WriteFile_ShouldCreateParentDirectories()
    {
        var tool = new WriteFileTool(() => _testDir);
        await tool.ExecuteAsync("""{"path":"sub/dir/file.txt","content":"nested"}""");

        File.Exists(Path.Combine(_testDir, "sub", "dir", "file.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task WriteFile_OutsideWorkspace_ShouldReturnError()
    {
        var tool = new WriteFileTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"path":"../../escape.txt","content":"hack"}""");

        result.Should().Contain("error");
        result.Should().Contain("outside the workspace");
    }

    // ---- ListFilesTool ----

    [Fact]
    public async Task ListFiles_ShouldListAllFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "a.cs"), "");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "b.txt"), "");

        var tool = new ListFilesTool(() => _testDir);
        var result = await tool.ExecuteAsync("{}");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ListFiles_WithPattern_ShouldFilter()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "a.cs"), "");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "b.txt"), "");

        var tool = new ListFilesTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"pattern":"*.cs"}""");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ListFiles_ShouldExcludeBinObj()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, "bin"));
        Directory.CreateDirectory(Path.Combine(_testDir, "obj"));
        await File.WriteAllTextAsync(Path.Combine(_testDir, "bin", "a.dll"), "");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "obj", "b.dll"), "");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "c.cs"), "");

        var tool = new ListFilesTool(() => _testDir);
        var result = await tool.ExecuteAsync("{}");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("count").GetInt32().Should().Be(1);
    }

    // ---- SearchFilesTool ----

    [Fact]
    public async Task SearchFiles_ShouldFindMatches()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "code.cs"), "public class Foo\n{\n    int bar = 42;\n}");

        var tool = new SearchFilesTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"query":"bar"}""");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("match_count").GetInt32().Should().Be(1);
        var match = parsed.GetProperty("matches")[0];
        match.GetProperty("line").GetInt32().Should().Be(3);
        match.GetProperty("content").GetString().Should().Contain("bar");
    }

    [Fact]
    public async Task SearchFiles_WithRegex_ShouldWork()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "data.txt"), "hello 123\nworld 456");

        var tool = new SearchFilesTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"query":"\\d{3}","is_regex":true}""");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("match_count").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task SearchFiles_NoMatches_ShouldReturnEmpty()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "code.cs"), "hello world");

        var tool = new SearchFilesTool(() => _testDir);
        var result = await tool.ExecuteAsync("""{"query":"nonexistent_string_xyz"}""");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("match_count").GetInt32().Should().Be(0);
    }

    // ---- ReadEditorTool ----

    [Fact]
    public async Task ReadEditor_ShouldReturnEditorState()
    {
        var state = new ReadEditorTool.EditorState
        {
            FilePath = "src/main.cs",
            FileName = "main.cs",
            Language = "CSharp",
            Content = "public class Main { }",
            CursorLine = 1,
            CursorColumn = 5
        };

        var tool = new ReadEditorTool(() => state);
        var result = await tool.ExecuteAsync("{}");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("file_path").GetString().Should().Be("src/main.cs");
        parsed.GetProperty("language").GetString().Should().Be("CSharp");
        parsed.GetProperty("content").GetString().Should().Contain("Main");
    }

    [Fact]
    public async Task ReadEditor_NoActiveEditor_ShouldReturnError()
    {
        var tool = new ReadEditorTool(() => null);
        var result = await tool.ExecuteAsync("{}");

        result.Should().Contain("error");
        result.Should().Contain("No editor");
    }

    [Fact]
    public async Task ReadEditor_WithSelection_ShouldIncludeIt()
    {
        var state = new ReadEditorTool.EditorState
        {
            FilePath = "test.cs",
            FileName = "test.cs",
            Content = "some code",
            SelectedText = "selected portion"
        };

        var tool = new ReadEditorTool(() => state);
        var result = await tool.ExecuteAsync("{}");

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("has_selection").GetBoolean().Should().BeTrue();
        parsed.GetProperty("selected_text").GetString().Should().Be("selected portion");
    }

    // ---- ApplyEditTool ----

    [Fact]
    public async Task ApplyEdit_ShouldCallDelegate()
    {
        ApplyEditTool.EditOperation? captured = null;
        var tool = new ApplyEditTool(op => { captured = op; return true; });

        var result = await tool.ExecuteAsync("""{"new_text":"hello","line_start":5,"line_end":10}""");

        captured.Should().NotBeNull();
        captured!.NewText.Should().Be("hello");
        captured.LineStart.Should().Be(5);
        captured.LineEnd.Should().Be(10);

        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ApplyEdit_ReplaceSelection_ShouldSetFlag()
    {
        ApplyEditTool.EditOperation? captured = null;
        var tool = new ApplyEditTool(op => { captured = op; return true; });

        await tool.ExecuteAsync("""{"new_text":"replaced","replace_selection":true}""");

        captured!.ReplaceSelection.Should().BeTrue();
    }
}

public sealed class PromptLoaderTests
{
    [Theory]
    [InlineData("general")]
    [InlineData("coding")]
    [InlineData("debugging")]
    [InlineData("testing")]
    public void GetPrompt_ShouldLoadTemplate(string templateName)
    {
        var prompt = PromptLoader.GetPrompt(templateName);

        prompt.Should().NotBeNullOrWhiteSpace();
        prompt.Length.Should().BeGreaterThan(100);
    }

    [Fact]
    public void GetPrompt_UnknownTemplate_ShouldFallbackToGeneral()
    {
        var prompt = PromptLoader.GetPrompt("nonexistent");
        var general = PromptLoader.GetPrompt("general");

        prompt.Should().Be(general);
    }

    [Fact]
    public void GetAvailableTemplates_ShouldReturnAllTemplates()
    {
        var templates = PromptLoader.GetAvailableTemplates();

        templates.Should().Contain("general");
        templates.Should().Contain("coding");
        templates.Should().Contain("debugging");
        templates.Should().Contain("testing");
    }

    [Fact]
    public void BuildPrompt_WithContext_ShouldAppendContextSection()
    {
        var context = new PromptContext
        {
            WorkspacePath = "/project",
            ActiveFilePath = "src/main.cs",
            ActiveFileLanguage = "CSharp"
        };

        var prompt = PromptLoader.BuildPrompt("general", context);

        prompt.Should().Contain("Current Context");
        prompt.Should().Contain("/project");
        prompt.Should().Contain("src/main.cs");
        prompt.Should().Contain("CSharp");
    }

    [Fact]
    public void BuildPrompt_WithoutContext_ShouldReturnBasePrompt()
    {
        var prompt = PromptLoader.BuildPrompt("general");
        var basePrompt = PromptLoader.GetPrompt("general");

        prompt.Should().Be(basePrompt);
    }

    [Fact]
    public void CodingPrompt_ShouldMentionConventions()
    {
        var prompt = PromptLoader.GetPrompt("coding");

        prompt.Should().Contain("sealed");
        prompt.Should().Contain("record");
    }

    [Fact]
    public void DebugPrompt_ShouldMentionCommonIssues()
    {
        var prompt = PromptLoader.GetPrompt("debugging");

        prompt.Should().Contain("NullReferenceException");
        prompt.Should().Contain("root cause");
    }

    [Fact]
    public void TestingPrompt_ShouldMentionTestFrameworks()
    {
        var prompt = PromptLoader.GetPrompt("testing");

        prompt.Should().Contain("xUnit");
        prompt.Should().Contain("FluentAssertions");
        prompt.Should().Contain("NSubstitute");
    }
}
