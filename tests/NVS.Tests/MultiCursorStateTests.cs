using NVS.Behaviors;

namespace NVS.Tests;

public class MultiCursorStateTests
{
    // --- Basic cursor management ---

    [Fact]
    public void AddCursor_ShouldAddNewCursor()
    {
        var state = new MultiCursorState();

        state.AddCursor(10);

        state.Cursors.Should().ContainSingle().Which.Should().Be(10);
        state.IsActive.Should().BeTrue();
    }

    [Fact]
    public void AddCursor_Duplicate_ShouldNotAddTwice()
    {
        var state = new MultiCursorState();

        state.AddCursor(10);
        state.AddCursor(10);

        state.Cursors.Should().HaveCount(1);
    }

    [Fact]
    public void AddCursor_MultipleCursors_ShouldBeSorted()
    {
        var state = new MultiCursorState();

        state.AddCursor(30);
        state.AddCursor(10);
        state.AddCursor(20);

        state.Cursors.Should().BeInAscendingOrder();
        state.Cursors.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void Clear_ShouldRemoveAllCursors()
    {
        var state = new MultiCursorState();
        state.AddCursor(10);
        state.AddCursor(20);

        state.Clear();

        state.Cursors.Should().BeEmpty();
        state.IsActive.Should().BeFalse();
    }

    [Fact]
    public void RemoveCursor_ShouldRemoveSpecificCursor()
    {
        var state = new MultiCursorState();
        state.AddCursor(10);
        state.AddCursor(20);

        state.RemoveCursor(10);

        state.Cursors.Should().ContainSingle().Which.Should().Be(20);
    }

    [Fact]
    public void IsActive_WhenNoCursors_ShouldBeFalse()
    {
        var state = new MultiCursorState();

        state.IsActive.Should().BeFalse();
    }

    // --- AddCursorFromLine ---

    [Fact]
    public void AddCursorFromLine_ShouldAddAtCorrectOffset()
    {
        var state = new MultiCursorState();

        var result = state.AddCursorFromLine(
            primaryOffset: 15,
            targetLineStartOffset: 30,
            targetLineLength: 20,
            columnInLine: 5);

        result.Should().Be(35);
        state.Cursors.Should().ContainSingle().Which.Should().Be(35);
    }

    [Fact]
    public void AddCursorFromLine_ColumnExceedsLineLength_ShouldClamp()
    {
        var state = new MultiCursorState();

        var result = state.AddCursorFromLine(
            primaryOffset: 15,
            targetLineStartOffset: 30,
            targetLineLength: 5,
            columnInLine: 20);

        result.Should().Be(35); // clamped to line end
    }

    [Fact]
    public void AddCursorFromLine_SameAsPrimary_ShouldReturnNull()
    {
        var state = new MultiCursorState();

        var result = state.AddCursorFromLine(
            primaryOffset: 35,
            targetLineStartOffset: 30,
            targetLineLength: 20,
            columnInLine: 5);

        result.Should().BeNull();
        state.Cursors.Should().BeEmpty();
    }

    [Fact]
    public void AddCursorFromLine_AlreadyExists_ShouldReturnNull()
    {
        var state = new MultiCursorState();
        state.AddCursor(35);

        var result = state.AddCursorFromLine(
            primaryOffset: 15,
            targetLineStartOffset: 30,
            targetLineLength: 20,
            columnInLine: 5);

        result.Should().BeNull();
        state.Cursors.Should().HaveCount(1);
    }

    // --- GetInsertEdits ---

    [Fact]
    public void GetInsertEdits_ShouldReturnEditsInReverseOrder()
    {
        var state = new MultiCursorState();
        state.AddCursor(10);
        state.AddCursor(20);

        var edits = state.GetInsertEdits(5, "x");

        // Primary insert at 5, so cursors shift: 10→11, 20→21
        edits.Should().HaveCount(2);
        edits[0].Offset.Should().Be(21); // highest first
        edits[1].Offset.Should().Be(11);
        edits[0].Text.Should().Be("x");
    }

    [Fact]
    public void GetInsertEdits_CursorBeforePrimary_ShouldNotShift()
    {
        var state = new MultiCursorState();
        state.AddCursor(3);

        var edits = state.GetInsertEdits(10, "ab");

        // Cursor at 3 is before primary at 10, no shift
        edits.Should().ContainSingle();
        edits[0].Offset.Should().Be(3);
    }

    [Fact]
    public void GetInsertEdits_CursorAfterPrimary_ShouldShift()
    {
        var state = new MultiCursorState();
        state.AddCursor(15);

        var edits = state.GetInsertEdits(10, "ab");

        // Cursor at 15 shifts by 2 (insert length) → 17
        edits.Should().ContainSingle();
        edits[0].Offset.Should().Be(17);
    }

    // --- GetBackspaceEdits ---

    [Fact]
    public void GetBackspaceEdits_ShouldReturnPositionsInReverseOrder()
    {
        var state = new MultiCursorState();
        state.AddCursor(10);
        state.AddCursor(20);

        var edits = state.GetBackspaceEdits(5);

        // Primary backspace at 5 shifts cursors >5 by -1: 10→9, 20→19
        edits.Should().HaveCount(2);
        edits[0].Should().Be(19); // highest first
        edits[1].Should().Be(9);
    }

    [Fact]
    public void GetBackspaceEdits_CursorBeforePrimary_ShouldNotShift()
    {
        var state = new MultiCursorState();
        state.AddCursor(3);

        var edits = state.GetBackspaceEdits(10);

        edits.Should().ContainSingle();
        edits[0].Should().Be(3);
    }

    // --- FindAllOccurrences ---

    [Fact]
    public void FindAllOccurrences_ShouldFindAll()
    {
        var text = "hello world hello everyone hello";

        var results = MultiCursorState.FindAllOccurrences(text, "hello");

        results.Should().Equal(0, 12, 27);
    }

    [Fact]
    public void FindAllOccurrences_EmptySearch_ShouldReturnEmpty()
    {
        var results = MultiCursorState.FindAllOccurrences("hello", "");

        results.Should().BeEmpty();
    }

    [Fact]
    public void FindAllOccurrences_NoMatches_ShouldReturnEmpty()
    {
        var results = MultiCursorState.FindAllOccurrences("hello world", "xyz");

        results.Should().BeEmpty();
    }

    // --- FindNextOccurrence ---

    [Fact]
    public void FindNextOccurrence_ShouldFindAfterOffset()
    {
        var text = "abc abc abc";

        var result = MultiCursorState.FindNextOccurrence(text, "abc", 5);

        result.Should().Be(8);
    }

    [Fact]
    public void FindNextOccurrence_ShouldWrapAround()
    {
        var text = "abc def abc";

        var result = MultiCursorState.FindNextOccurrence(text, "abc", 9);

        result.Should().Be(0);
    }

    [Fact]
    public void FindNextOccurrence_EmptySearch_ShouldReturnNegativeOne()
    {
        var result = MultiCursorState.FindNextOccurrence("hello", "", 0);

        result.Should().Be(-1);
    }

    [Fact]
    public void FindNextOccurrence_NotFound_ShouldReturnNegativeOne()
    {
        var result = MultiCursorState.FindNextOccurrence("hello world", "xyz", 0);

        result.Should().Be(-1);
    }
}
