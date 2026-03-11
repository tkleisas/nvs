using FluentAssertions;
using NVS.Core.Interfaces;
using NVS.Services.Git;

namespace NVS.Services.Tests;

public sealed class GitServiceParsePatchTests
{
    [Fact]
    public void ParsePatch_EmptyString_ReturnsEmpty()
    {
        var result = GitService.ParsePatch("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParsePatch_NullString_ReturnsEmpty()
    {
        var result = GitService.ParsePatch(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParsePatch_SingleHunkAddition_ParsesCorrectly()
    {
        var patch = "@@ -0,0 +1,3 @@\n+line 1\n+line 2\n+line 3\n";

        var hunks = GitService.ParsePatch(patch);

        hunks.Should().HaveCount(1);
        hunks[0].OldStart.Should().Be(0);
        hunks[0].OldCount.Should().Be(0);
        hunks[0].NewStart.Should().Be(1);
        hunks[0].NewCount.Should().Be(3);
        hunks[0].Lines.Should().HaveCount(3);
        hunks[0].Lines.Should().OnlyContain(l => l.Type == DiffLineType.Addition);
    }

    [Fact]
    public void ParsePatch_SingleHunkDeletion_ParsesCorrectly()
    {
        var patch = "@@ -1,2 +0,0 @@\n-removed 1\n-removed 2\n";

        var hunks = GitService.ParsePatch(patch);

        hunks.Should().HaveCount(1);
        hunks[0].OldStart.Should().Be(1);
        hunks[0].OldCount.Should().Be(2);
        hunks[0].Lines.Should().HaveCount(2);
        hunks[0].Lines.Should().OnlyContain(l => l.Type == DiffLineType.Deletion);
    }

    [Fact]
    public void ParsePatch_MixedChanges_ParsesAllLineTypes()
    {
        var patch = "@@ -1,4 +1,4 @@\n context line\n-old line\n+new line\n context line\n";

        var hunks = GitService.ParsePatch(patch);

        hunks.Should().HaveCount(1);
        var lines = hunks[0].Lines;
        lines.Should().HaveCount(4);
        lines[0].Type.Should().Be(DiffLineType.Context);
        lines[0].Content.Should().Be("context line");
        lines[1].Type.Should().Be(DiffLineType.Deletion);
        lines[1].Content.Should().Be("old line");
        lines[2].Type.Should().Be(DiffLineType.Addition);
        lines[2].Content.Should().Be("new line");
        lines[3].Type.Should().Be(DiffLineType.Context);
    }

    [Fact]
    public void ParsePatch_MultipleHunks_ParsesBothHunks()
    {
        var patch = "@@ -1,3 +1,3 @@\n context\n-old\n+new\n@@ -10,2 +10,3 @@\n context\n+added\n context\n";

        var hunks = GitService.ParsePatch(patch);

        hunks.Should().HaveCount(2);
        hunks[0].OldStart.Should().Be(1);
        hunks[0].Lines.Should().HaveCount(3);
        hunks[1].OldStart.Should().Be(10);
        hunks[1].Lines.Should().HaveCount(3);
    }

    [Fact]
    public void ParsePatch_LineNumbers_TrackedCorrectly()
    {
        var patch = "@@ -5,3 +5,4 @@\n context\n-deleted\n+added1\n+added2\n context\n";

        var hunks = GitService.ParsePatch(patch);

        var lines = hunks[0].Lines;

        // Context line: old=5, new=5
        lines[0].OldLineNumber.Should().Be(5);
        lines[0].NewLineNumber.Should().Be(5);

        // Deletion: old=6, new=-1
        lines[1].OldLineNumber.Should().Be(6);
        lines[1].NewLineNumber.Should().Be(-1);

        // Addition1: old=-1, new=6
        lines[2].OldLineNumber.Should().Be(-1);
        lines[2].NewLineNumber.Should().Be(6);

        // Addition2: old=-1, new=7
        lines[3].OldLineNumber.Should().Be(-1);
        lines[3].NewLineNumber.Should().Be(7);

        // Context: old=7, new=8
        lines[4].OldLineNumber.Should().Be(7);
        lines[4].NewLineNumber.Should().Be(8);
    }

    [Fact]
    public void ParsePatch_ContentPreservesText_StripsPrefix()
    {
        var patch = "@@ -1,1 +1,1 @@\n-hello world\n+hello universe\n";

        var hunks = GitService.ParsePatch(patch);

        hunks[0].Lines[0].Content.Should().Be("hello world");
        hunks[0].Lines[1].Content.Should().Be("hello universe");
    }

    [Fact]
    public void ParsePatch_HeaderWithFunctionContext_ParsesCorrectly()
    {
        // Real patches often have function context after the second @@
        var patch = "@@ -10,3 +10,4 @@ public void Method()\n context\n+new line\n context\n context\n";

        var hunks = GitService.ParsePatch(patch);

        hunks.Should().HaveCount(1);
        hunks[0].OldStart.Should().Be(10);
        hunks[0].NewStart.Should().Be(10);
    }

    [Fact]
    public void ParsePatch_SingleLineCount_DefaultsToOne()
    {
        // When count is omitted (e.g., "@@ -1 +1 @@"), it means count=1
        var patch = "@@ -1 +1 @@\n-old\n+new\n";

        var hunks = GitService.ParsePatch(patch);

        hunks.Should().HaveCount(1);
        hunks[0].OldCount.Should().Be(1);
        hunks[0].NewCount.Should().Be(1);
    }
}
