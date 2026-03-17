using AvaloniaEdit.Document;
using NVS.Behaviors;

namespace NVS.Tests;

public sealed class BraceFoldingStrategyTests
{
    private static TextDocument CreateDocument(string text) => new(text);

    [Fact]
    public void CreateFoldings_SimpleBlock_ShouldCreateOneFolding()
    {
        var doc = CreateDocument("class Foo {\n    int x;\n}");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().HaveCount(1);
        foldings[0].StartOffset.Should().Be(10); // {
        foldings[0].EndOffset.Should().Be(24); // after }
    }

    [Fact]
    public void CreateFoldings_NestedBlocks_ShouldCreateMultipleFoldings()
    {
        var doc = CreateDocument("class Foo {\n    void Bar() {\n        x++;\n    }\n}");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().HaveCount(2);
    }

    [Fact]
    public void CreateFoldings_SingleLineBraces_ShouldNotFold()
    {
        var doc = CreateDocument("if (x) { y++; }");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().BeEmpty();
    }

    [Fact]
    public void CreateFoldings_EmptyDocument_ShouldReturnEmpty()
    {
        var doc = CreateDocument("");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().BeEmpty();
    }

    [Fact]
    public void CreateFoldings_BracesInStringLiteral_ShouldBeIgnored()
    {
        var doc = CreateDocument("var s = \"{\n}\";");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().BeEmpty();
    }

    [Fact]
    public void CreateFoldings_BracesInLineComment_ShouldBeIgnored()
    {
        var doc = CreateDocument("// {\n// }");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().BeEmpty();
    }

    [Fact]
    public void CreateFoldings_BracesInBlockComment_ShouldBeIgnored()
    {
        var doc = CreateDocument("/* {\n } */");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().BeEmpty();
    }

    [Fact]
    public void CreateFoldings_RegionDirectives_ShouldFold()
    {
        var doc = CreateDocument("#region Fields\nint x;\nint y;\n#endregion");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().HaveCount(1);
        foldings[0].Name.Should().Be("Fields");
    }

    [Fact]
    public void CreateFoldings_MultipleBlocks_ShouldBeSortedByOffset()
    {
        var doc = CreateDocument("void A() {\n    x++;\n}\nvoid B() {\n    y++;\n}");
        var strategy = new BraceFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().HaveCount(2);
        foldings[0].StartOffset.Should().BeLessThan(foldings[1].StartOffset);
    }
}

public sealed class IndentFoldingStrategyTests
{
    private static TextDocument CreateDocument(string text) => new(text);

    [Fact]
    public void CreateFoldings_PythonFunction_ShouldFold()
    {
        var doc = CreateDocument("def foo():\n    x = 1\n    y = 2\n\nz = 3");
        var strategy = new IndentFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact]
    public void CreateFoldings_NestedPython_ShouldFoldMultiple()
    {
        var doc = CreateDocument("class Foo:\n    def bar(self):\n        x = 1\n        y = 2\n    def baz(self):\n        z = 3\n");
        var strategy = new IndentFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().HaveCountGreaterOrEqualTo(1);
    }

    [Fact]
    public void CreateFoldings_FlatDocument_ShouldReturnEmpty()
    {
        var doc = CreateDocument("x = 1\ny = 2\nz = 3");
        var strategy = new IndentFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().BeEmpty();
    }

    [Fact]
    public void CreateFoldings_EmptyDocument_ShouldReturnEmpty()
    {
        var doc = CreateDocument("");
        var strategy = new IndentFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().BeEmpty();
    }

    [Fact]
    public void CreateFoldings_YamlNestedKeys_ShouldFold()
    {
        var doc = CreateDocument("root:\n  child1: value1\n  child2: value2\nother: value3");
        var strategy = new IndentFoldingStrategy();

        var foldings = strategy.CreateFoldings(doc).ToList();

        foldings.Should().HaveCountGreaterOrEqualTo(1);
    }
}
