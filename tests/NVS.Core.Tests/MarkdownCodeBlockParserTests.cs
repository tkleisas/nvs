using FluentAssertions;
using NVS.Core.LLM;

namespace NVS.Core.Tests;

public sealed class MarkdownCodeBlockParserTests
{
    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var result = MarkdownCodeBlockParser.Parse("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NullString_ReturnsEmpty()
    {
        var result = MarkdownCodeBlockParser.Parse(null!);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PlainTextOnly_ReturnsSingleTextSegment()
    {
        var result = MarkdownCodeBlockParser.Parse("Hello, world!\nSecond line.");

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(SegmentType.Text);
        result[0].Content.Should().Be("Hello, world!\nSecond line.");
        result[0].Language.Should().BeNull();
    }

    [Fact]
    public void Parse_SingleCodeBlockWithLanguage_ReturnsCodeSegment()
    {
        var input = "```csharp\nvar x = 42;\nConsole.WriteLine(x);\n```";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(SegmentType.Code);
        result[0].Content.Should().Be("var x = 42;\nConsole.WriteLine(x);");
        result[0].Language.Should().Be("csharp");
    }

    [Fact]
    public void Parse_SingleCodeBlockWithoutLanguage_ReturnsCodeSegmentWithNullLanguage()
    {
        var input = "```\nsome code\n```";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(SegmentType.Code);
        result[0].Content.Should().Be("some code");
        result[0].Language.Should().BeNull();
    }

    [Fact]
    public void Parse_TextBeforeAndAfterCodeBlock_ReturnsThreeSegments()
    {
        var input = "Here is code:\n```python\nprint('hi')\n```\nAfter code.";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(3);
        result[0].Type.Should().Be(SegmentType.Text);
        result[0].Content.Should().Be("Here is code:");
        result[1].Type.Should().Be(SegmentType.Code);
        result[1].Content.Should().Be("print('hi')");
        result[1].Language.Should().Be("python");
        result[2].Type.Should().Be(SegmentType.Text);
        result[2].Content.Should().Be("After code.");
    }

    [Fact]
    public void Parse_MultipleCodeBlocks_ReturnsAlternatingSegments()
    {
        var input = "First:\n```js\nlet a = 1;\n```\nMiddle\n```sql\nSELECT 1;\n```\nEnd";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(5);
        result[0].Type.Should().Be(SegmentType.Text);
        result[1].Type.Should().Be(SegmentType.Code);
        result[1].Language.Should().Be("js");
        result[2].Type.Should().Be(SegmentType.Text);
        result[3].Type.Should().Be(SegmentType.Code);
        result[3].Language.Should().Be("sql");
        result[4].Type.Should().Be(SegmentType.Text);
    }

    [Fact]
    public void Parse_UnclosedCodeBlock_TreatsAsCode()
    {
        var input = "Starting:\n```json\n{\"key\": \"value\"}\n{\"more\": true}";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(2);
        result[0].Type.Should().Be(SegmentType.Text);
        result[0].Content.Should().Be("Starting:");
        result[1].Type.Should().Be(SegmentType.Code);
        result[1].Content.Should().Contain("\"key\"");
        result[1].Language.Should().Be("json");
    }

    [Fact]
    public void Parse_CodeBlockAtStartOfContent_NoLeadingText()
    {
        var input = "```go\nfmt.Println(\"hello\")\n```";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(SegmentType.Code);
        result[0].Language.Should().Be("go");
    }

    [Fact]
    public void Parse_IndentedCodeFence_StillRecognized()
    {
        var input = "  ```java\n  int x = 1;\n  ```";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(SegmentType.Code);
        result[0].Language.Should().Be("java");
    }

    [Fact]
    public void Parse_BackticksInsideCodeBlock_NotTreatedAsFence()
    {
        // Backticks that are not at the start of a line (inline code)
        var input = "```\nUse `var` for locals\n```";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(SegmentType.Code);
        result[0].Content.Should().Be("Use `var` for locals");
    }

    [Fact]
    public void Parse_EmptyCodeBlock_ReturnsCodeWithEmptyContent()
    {
        var input = "```\n```";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(SegmentType.Code);
        result[0].Content.Should().BeEmpty();
    }

    [Fact]
    public void Parse_LanguageTagWithSpaces_TrimsCorrectly()
    {
        var input = "```  typescript  \nconst x = 1;\n```";

        var result = MarkdownCodeBlockParser.Parse(input);

        result.Should().HaveCount(1);
        result[0].Language.Should().Be("typescript");
    }
}
