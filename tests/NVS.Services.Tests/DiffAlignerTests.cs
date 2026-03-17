using NVS.Core.Interfaces;
using NVS.Services.Git;

namespace NVS.Services.Tests;

public sealed class DiffAlignerTests
{
    [Fact]
    public void AlignHunks_EmptyList_ReturnsEmpty()
    {
        var result = DiffAligner.AlignHunks([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void AlignHunk_ContextOnly_PairsBothSides()
    {
        var hunk = new DiffHunk
        {
            OldStart = 1, OldCount = 3, NewStart = 1, NewCount = 3,
            Lines =
            [
                new DiffLine { Type = DiffLineType.Context, Content = "line1", OldLineNumber = 1, NewLineNumber = 1 },
                new DiffLine { Type = DiffLineType.Context, Content = "line2", OldLineNumber = 2, NewLineNumber = 2 },
                new DiffLine { Type = DiffLineType.Context, Content = "line3", OldLineNumber = 3, NewLineNumber = 3 },
            ],
        };

        var result = DiffAligner.AlignHunk(hunk);

        result.Should().HaveCount(3);
        result[0].Left.Type.Should().Be(DiffSideLineType.Context);
        result[0].Right.Type.Should().Be(DiffSideLineType.Context);
        result[0].Left.LineNumber.Should().Be(1);
        result[0].Right.LineNumber.Should().Be(1);
    }

    [Fact]
    public void AlignHunk_PureDeletion_LeftDeletedRightEmpty()
    {
        var hunk = new DiffHunk
        {
            OldStart = 1, OldCount = 2, NewStart = 1, NewCount = 0,
            Lines =
            [
                new DiffLine { Type = DiffLineType.Deletion, Content = "old1", OldLineNumber = 1, NewLineNumber = -1 },
                new DiffLine { Type = DiffLineType.Deletion, Content = "old2", OldLineNumber = 2, NewLineNumber = -1 },
            ],
        };

        var result = DiffAligner.AlignHunk(hunk);

        result.Should().HaveCount(2);
        result[0].Left.Type.Should().Be(DiffSideLineType.Deleted);
        result[0].Left.Content.Should().Be("old1");
        result[0].Right.Type.Should().Be(DiffSideLineType.Empty);
        result[1].Left.Content.Should().Be("old2");
    }

    [Fact]
    public void AlignHunk_PureAddition_LeftEmptyRightAdded()
    {
        var hunk = new DiffHunk
        {
            OldStart = 1, OldCount = 0, NewStart = 1, NewCount = 2,
            Lines =
            [
                new DiffLine { Type = DiffLineType.Addition, Content = "new1", OldLineNumber = -1, NewLineNumber = 1 },
                new DiffLine { Type = DiffLineType.Addition, Content = "new2", OldLineNumber = -1, NewLineNumber = 2 },
            ],
        };

        var result = DiffAligner.AlignHunk(hunk);

        result.Should().HaveCount(2);
        result[0].Left.Type.Should().Be(DiffSideLineType.Empty);
        result[0].Right.Type.Should().Be(DiffSideLineType.Added);
        result[0].Right.Content.Should().Be("new1");
    }

    [Fact]
    public void AlignHunk_Modification_PairsDeletionWithAddition()
    {
        var hunk = new DiffHunk
        {
            OldStart = 1, OldCount = 2, NewStart = 1, NewCount = 2,
            Lines =
            [
                new DiffLine { Type = DiffLineType.Deletion, Content = "old1", OldLineNumber = 1, NewLineNumber = -1 },
                new DiffLine { Type = DiffLineType.Deletion, Content = "old2", OldLineNumber = 2, NewLineNumber = -1 },
                new DiffLine { Type = DiffLineType.Addition, Content = "new1", OldLineNumber = -1, NewLineNumber = 1 },
                new DiffLine { Type = DiffLineType.Addition, Content = "new2", OldLineNumber = -1, NewLineNumber = 2 },
            ],
        };

        var result = DiffAligner.AlignHunk(hunk);

        result.Should().HaveCount(2);
        result[0].Left.Type.Should().Be(DiffSideLineType.Deleted);
        result[0].Left.Content.Should().Be("old1");
        result[0].Right.Type.Should().Be(DiffSideLineType.Added);
        result[0].Right.Content.Should().Be("new1");
        result[1].Left.Content.Should().Be("old2");
        result[1].Right.Content.Should().Be("new2");
    }

    [Fact]
    public void AlignHunk_UnequalModification_PadsWithEmpty()
    {
        // 3 deletions, 1 addition — should pad 2 empty on right
        var hunk = new DiffHunk
        {
            OldStart = 1, OldCount = 3, NewStart = 1, NewCount = 1,
            Lines =
            [
                new DiffLine { Type = DiffLineType.Deletion, Content = "a", OldLineNumber = 1, NewLineNumber = -1 },
                new DiffLine { Type = DiffLineType.Deletion, Content = "b", OldLineNumber = 2, NewLineNumber = -1 },
                new DiffLine { Type = DiffLineType.Deletion, Content = "c", OldLineNumber = 3, NewLineNumber = -1 },
                new DiffLine { Type = DiffLineType.Addition, Content = "x", OldLineNumber = -1, NewLineNumber = 1 },
            ],
        };

        var result = DiffAligner.AlignHunk(hunk);

        result.Should().HaveCount(3);
        result[0].Left.Content.Should().Be("a");
        result[0].Right.Content.Should().Be("x");
        result[1].Left.Content.Should().Be("b");
        result[1].Right.Type.Should().Be(DiffSideLineType.Empty);
        result[2].Left.Content.Should().Be("c");
        result[2].Right.Type.Should().Be(DiffSideLineType.Empty);
    }

    [Fact]
    public void AlignHunk_MixedContextAndChanges_PreservesOrder()
    {
        var hunk = new DiffHunk
        {
            OldStart = 1, OldCount = 4, NewStart = 1, NewCount = 4,
            Lines =
            [
                new DiffLine { Type = DiffLineType.Context, Content = "ctx1", OldLineNumber = 1, NewLineNumber = 1 },
                new DiffLine { Type = DiffLineType.Deletion, Content = "old", OldLineNumber = 2, NewLineNumber = -1 },
                new DiffLine { Type = DiffLineType.Addition, Content = "new", OldLineNumber = -1, NewLineNumber = 2 },
                new DiffLine { Type = DiffLineType.Context, Content = "ctx2", OldLineNumber = 3, NewLineNumber = 3 },
            ],
        };

        var result = DiffAligner.AlignHunk(hunk);

        result.Should().HaveCount(3);
        result[0].Left.Type.Should().Be(DiffSideLineType.Context);
        result[0].Left.Content.Should().Be("ctx1");
        result[1].Left.Type.Should().Be(DiffSideLineType.Deleted);
        result[1].Right.Type.Should().Be(DiffSideLineType.Added);
        result[2].Left.Type.Should().Be(DiffSideLineType.Context);
        result[2].Left.Content.Should().Be("ctx2");
    }

    [Fact]
    public void AlignHunks_MultipleHunks_ConcatenatesResults()
    {
        var hunks = new List<DiffHunk>
        {
            new()
            {
                OldStart = 1, OldCount = 1, NewStart = 1, NewCount = 1,
                Lines = [new DiffLine { Type = DiffLineType.Context, Content = "a", OldLineNumber = 1, NewLineNumber = 1 }],
            },
            new()
            {
                OldStart = 10, OldCount = 1, NewStart = 10, NewCount = 1,
                Lines = [new DiffLine { Type = DiffLineType.Deletion, Content = "b", OldLineNumber = 10, NewLineNumber = -1 }],
            },
        };

        var result = DiffAligner.AlignHunks(hunks);

        result.Should().HaveCount(2);
        result[0].Left.Content.Should().Be("a");
        result[1].Left.Content.Should().Be("b");
    }

    [Fact]
    public void AlignHunk_MoreAdditionsThanDeletions_PadsLeftWithEmpty()
    {
        var hunk = new DiffHunk
        {
            OldStart = 1, OldCount = 1, NewStart = 1, NewCount = 3,
            Lines =
            [
                new DiffLine { Type = DiffLineType.Deletion, Content = "old", OldLineNumber = 1, NewLineNumber = -1 },
                new DiffLine { Type = DiffLineType.Addition, Content = "new1", OldLineNumber = -1, NewLineNumber = 1 },
                new DiffLine { Type = DiffLineType.Addition, Content = "new2", OldLineNumber = -1, NewLineNumber = 2 },
                new DiffLine { Type = DiffLineType.Addition, Content = "new3", OldLineNumber = -1, NewLineNumber = 3 },
            ],
        };

        var result = DiffAligner.AlignHunk(hunk);

        result.Should().HaveCount(3);
        result[0].Left.Type.Should().Be(DiffSideLineType.Deleted);
        result[0].Right.Type.Should().Be(DiffSideLineType.Added);
        result[1].Left.Type.Should().Be(DiffSideLineType.Empty);
        result[1].Right.Type.Should().Be(DiffSideLineType.Added);
        result[2].Left.Type.Should().Be(DiffSideLineType.Empty);
        result[2].Right.Type.Should().Be(DiffSideLineType.Added);
    }

    [Fact]
    public void AlignHunk_StandaloneAddition_WithoutPrecedingDeletion()
    {
        var hunk = new DiffHunk
        {
            OldStart = 1, OldCount = 1, NewStart = 1, NewCount = 2,
            Lines =
            [
                new DiffLine { Type = DiffLineType.Context, Content = "ctx", OldLineNumber = 1, NewLineNumber = 1 },
                new DiffLine { Type = DiffLineType.Addition, Content = "inserted", OldLineNumber = -1, NewLineNumber = 2 },
            ],
        };

        var result = DiffAligner.AlignHunk(hunk);

        result.Should().HaveCount(2);
        result[0].Left.Type.Should().Be(DiffSideLineType.Context);
        result[1].Left.Type.Should().Be(DiffSideLineType.Empty);
        result[1].Right.Type.Should().Be(DiffSideLineType.Added);
        result[1].Right.Content.Should().Be("inserted");
    }
}
