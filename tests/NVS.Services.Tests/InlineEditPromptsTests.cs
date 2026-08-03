using NVS.Services.LLM;

namespace NVS.Services.Tests;

public class SimpleDifferTests
{
    [Fact]
    public void Diff_IdenticalTexts_AllContext()
    {
        var rows = SimpleDiffer.Diff("a\nb", "a\nb");

        rows.Should().OnlyContain(r => r.Kind == DiffRowKind.Context);
    }

    [Fact]
    public void Diff_AddedLine_MarkedAdded()
    {
        var rows = SimpleDiffer.Diff("a\nc", "a\nb\nc");

        rows.Should().Contain(r => r.Kind == DiffRowKind.Added && r.Text == "b");
    }

    [Fact]
    public void Diff_RemovedLine_MarkedDeleted()
    {
        var rows = SimpleDiffer.Diff("a\nb\nc", "a\nc");

        rows.Should().Contain(r => r.Kind == DiffRowKind.Deleted && r.Text == "b");
    }

    [Fact]
    public void Diff_ChangedBlock_DeletedThenAdded()
    {
        var rows = SimpleDiffer.Diff("x = 1;", "x = 2;");

        rows.Should().Contain(r => r.Kind == DiffRowKind.Deleted && r.Text == "x = 1;");
        rows.Should().Contain(r => r.Kind == DiffRowKind.Added && r.Text == "x = 2;");
    }

    [Fact]
    public void Diff_LargeUnchangedRegion_CollapsesContext()
    {
        var oldText = string.Join('\n', Enumerable.Range(1, 100).Select(i => $"line{i}"));
        var newText = string.Join('\n', Enumerable.Range(1, 100).Select(i => i == 50 ? "CHANGED" : $"line{i}"));

        var rows = SimpleDiffer.Diff(oldText, newText, contextLines: 2);

        rows.Count.Should().BeLessThan(100);
        rows.Should().Contain(r => r.Text == "CHANGED");
    }

    [Fact]
    public void Prefix_MatchesKind()
    {
        new DiffRow(DiffRowKind.Added, null, 1, "x").Prefix.Should().Be("+ ");
        new DiffRow(DiffRowKind.Deleted, 1, null, "x").Prefix.Should().Be("- ");
        new DiffRow(DiffRowKind.Context, 1, 1, "x").Prefix.Should().Be("  ");
    }
}

public class InlineEditPromptsTests
{
    [Fact]
    public void Build_WithContext_IncludesInstructionAndCode()
    {
        var (system, user) = InlineEditPrompts.Build("make it async", "void Foo() {}", "CSharp", hasContext: true);

        system.Should().Contain("CSharp");
        user.Should().Contain("make it async");
        user.Should().Contain("void Foo() {}");
    }

    [Fact]
    public void Build_NoContext_AsksForFreshCode()
    {
        var (_, user) = InlineEditPrompts.Build("write a hello world", "", "Python", hasContext: false);

        user.Should().Contain("hello world");
        user.Should().Contain("caret");
    }

    [Fact]
    public void ExtractCode_FencedBlock_ReturnsContents()
    {
        InlineEditPrompts.ExtractCode("Here:\n```csharp\nvar x = 1;\n```\nDone")
            .Should().Be("var x = 1;");
    }

    [Fact]
    public void ExtractCode_NoFence_ReturnsTrimmed()
    {
        InlineEditPrompts.ExtractCode("  var x = 1;  ").Should().Be("var x = 1;");
    }
}
