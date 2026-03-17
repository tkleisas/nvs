using NVS.Behaviors;

namespace NVS.Tests;

public sealed class BracketMatcherTests
{
    [Fact]
    public void FindMatchingBracket_AtOpenParen_ShouldFindClosing()
    {
        var text = "foo(bar)";
        var result = BracketMatcher.FindMatchingBracket(text, 3);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(3);
        result.CloseOffset.Should().Be(7);
    }

    [Fact]
    public void FindMatchingBracket_AtCloseParen_ShouldFindOpening()
    {
        var text = "foo(bar)";
        var result = BracketMatcher.FindMatchingBracket(text, 7);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(3);
        result.CloseOffset.Should().Be(7);
    }

    [Fact]
    public void FindMatchingBracket_NestedBraces_ShouldMatchCorrectPair()
    {
        var text = "if (a) { if (b) { c(); } }";
        // Caret at the outer opening brace (offset 7)
        var result = BracketMatcher.FindMatchingBracket(text, 7);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(7);
        result.CloseOffset.Should().Be(25);
    }

    [Fact]
    public void FindMatchingBracket_NestedBraces_InnerPair()
    {
        var text = "if (a) { if (b) { c(); } }";
        // Caret at the inner opening brace (offset 16)
        var result = BracketMatcher.FindMatchingBracket(text, 16);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(16);
        result.CloseOffset.Should().Be(23);
    }

    [Fact]
    public void FindMatchingBracket_SquareBrackets_ShouldMatch()
    {
        var text = "arr[i + 1]";
        var result = BracketMatcher.FindMatchingBracket(text, 3);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(3);
        result.CloseOffset.Should().Be(9);
    }

    [Fact]
    public void FindMatchingBracket_CaretAfterBracket_ShouldMatchPrevious()
    {
        // Caret at offset 4, the char before is ')' at offset 3
        var text = "f(x)";
        var result = BracketMatcher.FindMatchingBracket(text, 4);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(1);
        result.CloseOffset.Should().Be(3);
    }

    [Fact]
    public void FindMatchingBracket_NoBracketAtCaret_ShouldReturnNull()
    {
        var text = "hello world";
        var result = BracketMatcher.FindMatchingBracket(text, 5);

        result.Should().BeNull();
    }

    [Fact]
    public void FindMatchingBracket_UnmatchedOpen_ShouldReturnNull()
    {
        var text = "foo(bar";
        var result = BracketMatcher.FindMatchingBracket(text, 3);

        result.Should().BeNull();
    }

    [Fact]
    public void FindMatchingBracket_UnmatchedClose_ShouldReturnNull()
    {
        var text = "foo)bar";
        var result = BracketMatcher.FindMatchingBracket(text, 3);

        result.Should().BeNull();
    }

    [Fact]
    public void FindMatchingBracket_EmptyText_ShouldReturnNull()
    {
        var result = BracketMatcher.FindMatchingBracket("", 0);
        result.Should().BeNull();
    }

    [Fact]
    public void FindMatchingBracket_NullText_ShouldReturnNull()
    {
        var result = BracketMatcher.FindMatchingBracket(null!, 0);
        result.Should().BeNull();
    }

    [Fact]
    public void FindMatchingBracket_NegativeOffset_ShouldReturnNull()
    {
        var result = BracketMatcher.FindMatchingBracket("()", -1);
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("()", 0, 0, 1)]
    [InlineData("{}", 0, 0, 1)]
    [InlineData("[]", 0, 0, 1)]
    public void FindMatchingBracket_SimplePairs_ShouldMatch(string text, int caret, int expectedOpen, int expectedClose)
    {
        var result = BracketMatcher.FindMatchingBracket(text, caret);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(expectedOpen);
        result.CloseOffset.Should().Be(expectedClose);
    }

    [Fact]
    public void FindMatchingBracket_MixedBracketTypes_ShouldMatchCorrectType()
    {
        var text = "a[(b + c)]";
        // At '[' (offset 1)
        var result = BracketMatcher.FindMatchingBracket(text, 1);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(1);
        result.CloseOffset.Should().Be(9);
    }

    [Fact]
    public void FindMatchingBracket_DeeplyNested_ShouldMatchCorrectly()
    {
        var text = "a(b(c(d)))";
        // At innermost '(' (offset 5)
        var result = BracketMatcher.FindMatchingBracket(text, 5);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(5);
        result.CloseOffset.Should().Be(7);
    }

    [Fact]
    public void FindMatchingBracket_DeeplyNested_OutermostClose()
    {
        var text = "a(b(c(d)))";
        // At outermost ')' (offset 9)
        var result = BracketMatcher.FindMatchingBracket(text, 9);

        result.Should().NotBeNull();
        result!.OpenOffset.Should().Be(1);
        result.CloseOffset.Should().Be(9);
    }
}
