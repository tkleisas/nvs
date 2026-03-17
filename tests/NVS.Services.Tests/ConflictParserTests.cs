using NVS.Core.Interfaces;
using NVS.Services.Git;

namespace NVS.Services.Tests;

public sealed class ConflictParserTests
{
    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var result = ConflictParser.Parse("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoConflicts_ReturnsEmpty()
    {
        var content = "line1\nline2\nline3\n";
        var result = ConflictParser.Parse(content);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SingleConflict_ReturnsOneBlock()
    {
        var content = """
            some code
            <<<<<<< HEAD
            our change
            =======
            their change
            >>>>>>> feature-branch
            more code
            """;

        var result = ConflictParser.Parse(content);

        result.Should().HaveCount(1);
        result[0].OursLines.Should().ContainSingle()
            .Which.Should().Contain("our change");
        result[0].TheirsLines.Should().ContainSingle()
            .Which.Should().Contain("their change");
        result[0].OursLabel.Should().Be("HEAD");
        result[0].TheirsLabel.Should().Be("feature-branch");
    }

    [Fact]
    public void Parse_MultipleConflicts_ReturnsAllBlocks()
    {
        var content = "<<<<<<< HEAD\nours1\n=======\ntheirs1\n>>>>>>> b1\nok\n<<<<<<< HEAD\nours2\n=======\ntheirs2\n>>>>>>> b2\n";

        var result = ConflictParser.Parse(content);

        result.Should().HaveCount(2);
        result[0].OursLines.Should().ContainSingle().Which.Should().Contain("ours1");
        result[0].TheirsLines.Should().ContainSingle().Which.Should().Contain("theirs1");
        result[1].OursLines.Should().ContainSingle().Which.Should().Contain("ours2");
        result[1].TheirsLines.Should().ContainSingle().Which.Should().Contain("theirs2");
    }

    [Fact]
    public void Parse_MultiLineConflict_CapturesAllLines()
    {
        var content = "<<<<<<< HEAD\nline1\nline2\nline3\n=======\nlineA\nlineB\n>>>>>>> other\n";

        var result = ConflictParser.Parse(content);

        result.Should().HaveCount(1);
        result[0].OursLines.Should().HaveCount(3);
        result[0].TheirsLines.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_EmptyOursSide_ReturnsEmptyOursLines()
    {
        var content = "<<<<<<< HEAD\n=======\ntheir line\n>>>>>>> other\n";

        var result = ConflictParser.Parse(content);

        result.Should().HaveCount(1);
        result[0].OursLines.Should().BeEmpty();
        result[0].TheirsLines.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_EmptyTheirsSide_ReturnsEmptyTheirsLines()
    {
        var content = "<<<<<<< HEAD\nour line\n=======\n>>>>>>> other\n";

        var result = ConflictParser.Parse(content);

        result.Should().HaveCount(1);
        result[0].OursLines.Should().HaveCount(1);
        result[0].TheirsLines.Should().BeEmpty();
    }

    [Fact]
    public void Parse_TracksLineNumbers()
    {
        var content = "line0\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> branch\nline6\n";

        var result = ConflictParser.Parse(content);

        result.Should().HaveCount(1);
        result[0].StartLine.Should().Be(1);
        result[0].EndLine.Should().Be(5);
    }

    [Fact]
    public void ResolveConflicts_AcceptCurrent_KeepsOurs()
    {
        var content = "before\n<<<<<<< HEAD\nour code\n=======\ntheir code\n>>>>>>> branch\nafter\n";
        var resolutions = new Dictionary<int, ConflictResolution>
        {
            [1] = ConflictResolution.AcceptCurrent,
        };

        var result = ConflictParser.ResolveConflicts(content, resolutions);

        result.Should().Contain("our code");
        result.Should().NotContain("their code");
        result.Should().NotContain("<<<<<<<");
        result.Should().Contain("before");
        result.Should().Contain("after");
    }

    [Fact]
    public void ResolveConflicts_AcceptIncoming_KeepsTheirs()
    {
        var content = "before\n<<<<<<< HEAD\nour code\n=======\ntheir code\n>>>>>>> branch\nafter\n";
        var resolutions = new Dictionary<int, ConflictResolution>
        {
            [1] = ConflictResolution.AcceptIncoming,
        };

        var result = ConflictParser.ResolveConflicts(content, resolutions);

        result.Should().NotContain("our code");
        result.Should().Contain("their code");
        result.Should().NotContain("<<<<<<<");
    }

    [Fact]
    public void ResolveConflicts_AcceptBoth_KeepsBothSides()
    {
        var content = "before\n<<<<<<< HEAD\nour code\n=======\ntheir code\n>>>>>>> branch\nafter\n";
        var resolutions = new Dictionary<int, ConflictResolution>
        {
            [1] = ConflictResolution.AcceptBoth,
        };

        var result = ConflictParser.ResolveConflicts(content, resolutions);

        result.Should().Contain("our code");
        result.Should().Contain("their code");
        result.Should().NotContain("<<<<<<<");
    }

    [Fact]
    public void ResolveConflicts_NoResolution_KeepsMarkers()
    {
        var content = "before\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> branch\nafter\n";
        var resolutions = new Dictionary<int, ConflictResolution>();

        var result = ConflictParser.ResolveConflicts(content, resolutions);

        result.Should().Contain("<<<<<<<");
        result.Should().Contain("=======");
        result.Should().Contain(">>>>>>>");
    }

    [Fact]
    public void ResolveConflicts_MultipleConflicts_ResolvesEachIndependently()
    {
        var content = "<<<<<<< HEAD\na\n=======\nb\n>>>>>>> x\nmiddle\n<<<<<<< HEAD\nc\n=======\nd\n>>>>>>> y\n";
        var resolutions = new Dictionary<int, ConflictResolution>
        {
            [0] = ConflictResolution.AcceptCurrent,
            [6] = ConflictResolution.AcceptIncoming,
        };

        var result = ConflictParser.ResolveConflicts(content, resolutions);

        result.Should().Contain("a");
        result.Should().NotContain("b");
        result.Should().Contain("middle");
        result.Should().NotContain("c");
        result.Should().Contain("d");
    }

    [Fact]
    public void Parse_NoLabels_ReturnsNullLabels()
    {
        var content = "<<<<<<< \nours\n=======\ntheirs\n>>>>>>> \n";

        var result = ConflictParser.Parse(content);

        result.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_WindowsLineEndings_WorksCorrectly()
    {
        var content = "<<<<<<< HEAD\r\nour code\r\n=======\r\ntheir code\r\n>>>>>>> branch\r\n";

        var result = ConflictParser.Parse(content);

        result.Should().HaveCount(1);
        result[0].OursLabel.Should().Be("HEAD");
        result[0].TheirsLabel.Should().Be("branch");
    }
}
