using NVS.Core.Interfaces;
using NVS.Services.Debug;

namespace NVS.Services.Tests;

public sealed class BreakpointStoreTests
{
    private readonly BreakpointStore _store = new();

    // ── Toggle ─────────────────────────────────────────────────────────

    [Fact]
    public void ToggleBreakpoint_OnNewLine_ShouldAddBreakpoint()
    {
        var bp = _store.ToggleBreakpoint("/src/Program.cs", 10);

        bp.Path.Should().Be("/src/Program.cs");
        bp.Line.Should().Be(10);
        bp.IsEnabled.Should().BeTrue();
        bp.IsVerified.Should().BeFalse();
    }

    [Fact]
    public void ToggleBreakpoint_OnExistingLine_ShouldRemoveBreakpoint()
    {
        _store.ToggleBreakpoint("/src/Program.cs", 10);
        _store.ToggleBreakpoint("/src/Program.cs", 10);

        _store.GetBreakpoints("/src/Program.cs").Should().BeEmpty();
    }

    [Fact]
    public void ToggleBreakpoint_ShouldRaiseAddedEvent()
    {
        BreakpointChangedEventArgs? args = null;
        _store.BreakpointChanged += (_, e) => args = e;

        _store.ToggleBreakpoint("/src/Program.cs", 10);

        args.Should().NotBeNull();
        args!.Kind.Should().Be(BreakpointChangeKind.Added);
        args.Breakpoint.Line.Should().Be(10);
    }

    [Fact]
    public void ToggleBreakpoint_WhenRemoving_ShouldRaiseRemovedEvent()
    {
        _store.ToggleBreakpoint("/src/Program.cs", 10);

        BreakpointChangedEventArgs? args = null;
        _store.BreakpointChanged += (_, e) => args = e;

        _store.ToggleBreakpoint("/src/Program.cs", 10);

        args.Should().NotBeNull();
        args!.Kind.Should().Be(BreakpointChangeKind.Removed);
    }

    // ── Get ─────────────────────────────────────────────────────────

    [Fact]
    public void GetBreakpoints_ShouldReturnFileBreakpoints()
    {
        _store.ToggleBreakpoint("/src/Program.cs", 10);
        _store.ToggleBreakpoint("/src/Program.cs", 20);
        _store.ToggleBreakpoint("/src/Other.cs", 5);

        var bps = _store.GetBreakpoints("/src/Program.cs");

        bps.Should().HaveCount(2);
        bps.Select(b => b.Line).Should().Contain([10, 20]);
    }

    [Fact]
    public void GetBreakpoints_ForUnknownFile_ShouldReturnEmpty()
    {
        var bps = _store.GetBreakpoints("/nonexistent.cs");
        bps.Should().BeEmpty();
    }

    [Fact]
    public void GetAllBreakpoints_ShouldReturnAll()
    {
        _store.ToggleBreakpoint("/src/A.cs", 1);
        _store.ToggleBreakpoint("/src/B.cs", 2);
        _store.ToggleBreakpoint("/src/A.cs", 3);

        var all = _store.GetAllBreakpoints();
        all.Should().HaveCount(3);
    }

    // ── Remove ───────────────────────────────────────────────────────

    [Fact]
    public void RemoveBreakpoint_ShouldRemoveSpecificLine()
    {
        _store.ToggleBreakpoint("/src/Program.cs", 10);
        _store.ToggleBreakpoint("/src/Program.cs", 20);

        _store.RemoveBreakpoint("/src/Program.cs", 10);

        var bps = _store.GetBreakpoints("/src/Program.cs");
        bps.Should().HaveCount(1);
        bps[0].Line.Should().Be(20);
    }

    [Fact]
    public void RemoveBreakpoint_NonExistent_ShouldNotThrow()
    {
        var act = () => _store.RemoveBreakpoint("/src/Program.cs", 99);
        act.Should().NotThrow();
    }

    // ── Clear ─────────────────────────────────────────────────────────

    [Fact]
    public void ClearBreakpoints_ShouldRemoveAllForFile()
    {
        _store.ToggleBreakpoint("/src/Program.cs", 10);
        _store.ToggleBreakpoint("/src/Program.cs", 20);
        _store.ToggleBreakpoint("/src/Other.cs", 5);

        _store.ClearBreakpoints("/src/Program.cs");

        _store.GetBreakpoints("/src/Program.cs").Should().BeEmpty();
        _store.GetBreakpoints("/src/Other.cs").Should().HaveCount(1);
    }

    [Fact]
    public void ClearAllBreakpoints_ShouldRemoveEverything()
    {
        _store.ToggleBreakpoint("/src/A.cs", 1);
        _store.ToggleBreakpoint("/src/B.cs", 2);

        _store.ClearAllBreakpoints();

        _store.GetAllBreakpoints().Should().BeEmpty();
    }

    [Fact]
    public void ClearBreakpoints_ShouldRaiseRemovedEventsForEach()
    {
        _store.ToggleBreakpoint("/src/Program.cs", 10);
        _store.ToggleBreakpoint("/src/Program.cs", 20);

        var removedLines = new List<int>();
        _store.BreakpointChanged += (_, e) =>
        {
            if (e.Kind == BreakpointChangeKind.Removed)
                removedLines.Add(e.Breakpoint.Line);
        };

        _store.ClearBreakpoints("/src/Program.cs");

        removedLines.Should().HaveCount(2);
        removedLines.Should().Contain([10, 20]);
    }

    // ── Verified Status ──────────────────────────────────────────────

    [Fact]
    public void UpdateVerifiedStatus_ShouldUpdateBreakpoint()
    {
        _store.ToggleBreakpoint("/src/Program.cs", 10);

        _store.UpdateVerifiedStatus("/src/Program.cs", 10, true);

        var bp = _store.GetBreakpoints("/src/Program.cs")[0];
        bp.IsVerified.Should().BeTrue();
    }

    [Fact]
    public void UpdateVerifiedStatus_ShouldRaiseUpdatedEvent()
    {
        _store.ToggleBreakpoint("/src/Program.cs", 10);

        BreakpointChangedEventArgs? args = null;
        _store.BreakpointChanged += (_, e) => args = e;

        _store.UpdateVerifiedStatus("/src/Program.cs", 10, true);

        args.Should().NotBeNull();
        args!.Kind.Should().Be(BreakpointChangeKind.Updated);
        args.Breakpoint.IsVerified.Should().BeTrue();
    }

    [Fact]
    public void UpdateVerifiedStatus_NonExistentLine_ShouldNotThrow()
    {
        var act = () => _store.UpdateVerifiedStatus("/src/Program.cs", 99, true);
        act.Should().NotThrow();
    }

    // ── Case Insensitive Path ──────────────────────────────────────────

    [Fact]
    public void GetBreakpoints_ShouldBeCaseInsensitive()
    {
        _store.ToggleBreakpoint("/src/Program.cs", 10);

        var bps = _store.GetBreakpoints("/SRC/PROGRAM.CS");
        bps.Should().HaveCount(1);
    }

    // ── Unique IDs ───────────────────────────────────────────────────

    [Fact]
    public void ToggleBreakpoint_ShouldAssignUniqueIds()
    {
        var bp1 = _store.ToggleBreakpoint("/src/A.cs", 1);
        var bp2 = _store.ToggleBreakpoint("/src/A.cs", 2);

        bp1.Id.Should().NotBe(bp2.Id);
        bp1.Id.Should().NotBe(Guid.Empty);
    }
}
