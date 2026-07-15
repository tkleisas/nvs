using FluentAssertions;
using NVS.Core.LLM;
using Xunit;

namespace NVS.Core.Tests;

public class MarkdownTextParserTests
{
    [Fact]
    public void Parse_PlainText_ReturnsSingleParagraph()
    {
        var blocks = MarkdownTextParser.Parse("hello world");

        blocks.Should().ContainSingle();
        blocks[0].Type.Should().Be(MarkdownBlockType.Paragraph);
        blocks[0].Spans.Should().ContainSingle().Which.Text.Should().Be("hello world");
    }

    [Fact]
    public void Parse_EmptyText_ReturnsNoBlocks()
    {
        MarkdownTextParser.Parse("").Should().BeEmpty();
    }

    [Fact]
    public void Parse_ConsecutiveLines_MergeIntoOneParagraph()
    {
        var blocks = MarkdownTextParser.Parse("line one\nline two\n\nline three");

        blocks.Should().HaveCount(2);
        blocks[0].Spans[0].Text.Should().Be("line one\nline two");
        blocks[1].Spans[0].Text.Should().Be("line three");
    }

    [Theory]
    [InlineData("# Title", 1)]
    [InlineData("## Section", 2)]
    [InlineData("### Sub", 3)]
    [InlineData("###### Deep", 6)]
    public void Parse_Heading_SetsLevel(string input, int expectedLevel)
    {
        var blocks = MarkdownTextParser.Parse(input);

        blocks.Should().ContainSingle();
        blocks[0].Type.Should().Be(MarkdownBlockType.Heading);
        blocks[0].HeadingLevel.Should().Be(expectedLevel);
    }

    [Fact]
    public void Parse_HashWithoutSpace_IsNotAHeading()
    {
        var blocks = MarkdownTextParser.Parse("#hashtag");

        blocks[0].Type.Should().Be(MarkdownBlockType.Paragraph);
    }

    [Fact]
    public void Parse_BulletList_ProducesListItems()
    {
        var blocks = MarkdownTextParser.Parse("- first\n- second\n* third");

        blocks.Should().HaveCount(3);
        blocks.Should().OnlyContain(b => b.Type == MarkdownBlockType.ListItem && b.ListMarker == "•");
    }

    [Fact]
    public void Parse_NumberedList_KeepsNumbers()
    {
        var blocks = MarkdownTextParser.Parse("1. first\n2. second\n10) tenth");

        blocks.Should().HaveCount(3);
        blocks[0].ListMarker.Should().Be("1.");
        blocks[1].ListMarker.Should().Be("2.");
        blocks[2].ListMarker.Should().Be("10.");
    }

    [Fact]
    public void Parse_NestedListItem_SetsIndentLevel()
    {
        var blocks = MarkdownTextParser.Parse("- top\n  - nested");

        blocks[0].IndentLevel.Should().Be(0);
        blocks[1].IndentLevel.Should().Be(1);
    }

    [Fact]
    public void Parse_Quote_ProducesQuoteBlock()
    {
        var blocks = MarkdownTextParser.Parse("> quoted text");

        blocks.Should().ContainSingle();
        blocks[0].Type.Should().Be(MarkdownBlockType.Quote);
        blocks[0].Spans[0].Text.Should().Be("quoted text");
    }

    [Fact]
    public void Parse_HorizontalRule_ProducesRuleBlock()
    {
        var blocks = MarkdownTextParser.Parse("---");

        blocks.Should().ContainSingle().Which.Type.Should().Be(MarkdownBlockType.HorizontalRule);
    }

    [Fact]
    public void ParseInlines_Bold_TogglesOnDoubleAsterisk()
    {
        var spans = MarkdownTextParser.ParseInlines("normal **bold** normal");

        spans.Should().HaveCount(3);
        spans[0].Bold.Should().BeFalse();
        spans[1].Should().Match<MarkdownSpan>(s => s.Bold && s.Text == "bold");
        spans[2].Bold.Should().BeFalse();
    }

    [Fact]
    public void ParseInlines_Italic_TogglesOnSingleAsterisk()
    {
        var spans = MarkdownTextParser.ParseInlines("*italic* rest");

        spans[0].Should().Match<MarkdownSpan>(s => s.Italic && s.Text == "italic");
        spans[1].Italic.Should().BeFalse();
    }

    [Fact]
    public void ParseInlines_InlineCode_IsNotStyledFurther()
    {
        var spans = MarkdownTextParser.ParseInlines("use `**not bold**` here");

        spans.Should().HaveCount(3);
        spans[1].Should().Match<MarkdownSpan>(s => s.Code && s.Text == "**not bold**" && !s.Bold);
    }

    [Fact]
    public void ParseInlines_SnakeCaseIdentifiers_AreNotItalicized()
    {
        var spans = MarkdownTextParser.ParseInlines("the file_name_here variable");

        spans.Should().ContainSingle().Which.Text.Should().Be("the file_name_here variable");
    }

    [Fact]
    public void ParseInlines_Link_CapturesTextAndUrl()
    {
        var spans = MarkdownTextParser.ParseInlines("see [the docs](https://example.com) for info");

        spans.Should().HaveCount(3);
        spans[1].Text.Should().Be("the docs");
        spans[1].LinkUrl.Should().Be("https://example.com");
    }

    [Fact]
    public void ParseInlines_Strikethrough_TogglesOnDoubleTilde()
    {
        var spans = MarkdownTextParser.ParseInlines("~~gone~~ kept");

        spans[0].Should().Match<MarkdownSpan>(s => s.Strikethrough && s.Text == "gone");
        spans[1].Strikethrough.Should().BeFalse();
    }

    [Fact]
    public void ParseInlines_UnclosedBacktick_IsLiteralText()
    {
        var spans = MarkdownTextParser.ParseInlines("a ` stray backtick");

        spans.Should().ContainSingle().Which.Text.Should().Be("a ` stray backtick");
    }

    [Fact]
    public void ParseInlines_BoldAndItalicCombined()
    {
        var spans = MarkdownTextParser.ParseInlines("***both***");

        // *** toggles bold then italic
        spans.Should().ContainSingle().Which.Should().Match<MarkdownSpan>(s => s.Bold && s.Italic && s.Text == "both");
    }
}
