using NVS.ViewModels;

namespace NVS.Tests;

public class SearchMatcherTests
{
    [Fact]
    public void PlainMatcher_IgnoresCaseByDefault()
    {
        var match = SearchViewModel.BuildMatcher("hello", matchCase: false, wholeWord: false, useRegex: false);

        match("say HELLO world").Should().BeTrue();
        match("goodbye").Should().BeFalse();
    }

    [Fact]
    public void PlainMatcher_MatchCaseRespected()
    {
        var match = SearchViewModel.BuildMatcher("hello", matchCase: true, wholeWord: false, useRegex: false);

        match("say HELLO world").Should().BeFalse();
        match("say hello world").Should().BeTrue();
    }

    [Fact]
    public void WholeWord_RequiresWordBoundaries()
    {
        var match = SearchViewModel.BuildMatcher("word", matchCase: false, wholeWord: true, useRegex: false);

        match("a word here").Should().BeTrue();
        match("swordplay").Should().BeFalse();
    }

    [Fact]
    public void Regex_MatchesPattern()
    {
        var match = SearchViewModel.BuildMatcher(@"foo\(\d+\)", matchCase: false, wholeWord: false, useRegex: true);

        match("call foo(42) now").Should().BeTrue();
        match("call foo(x) now").Should().BeFalse();
    }

    [Fact]
    public void Regex_InvalidPattern_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            SearchViewModel.BuildMatcher("([unclosed", false, false, true));
    }

    [Fact]
    public void WholeWord_EscapesRegexMetacharacters()
    {
        var match = SearchViewModel.BuildMatcher("a.b", matchCase: false, wholeWord: true, useRegex: false);

        match("literal a.b").Should().BeTrue();
        match("literal axb").Should().BeFalse();
    }
}
