using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Services.Lsp;
using NVS.Services.Lsp.Protocol;
using Range = NVS.Core.Models.Range;

namespace NVS.Services.Tests;

public sealed class LspModelMapperTests
{
    // ─── To LSP ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToLspPosition_ShouldMapCorrectly()
    {
        var position = new Position { Line = 10, Column = 5 };

        var result = LspModelMapper.ToLspPosition(position);

        result.Line.Should().Be(10);
        result.Character.Should().Be(5);
    }

    [Fact]
    public void ToLspRange_ShouldMapStartAndEnd()
    {
        var range = new Range
        {
            Start = new Position { Line = 1, Column = 2 },
            End = new Position { Line = 3, Column = 4 },
        };

        var result = LspModelMapper.ToLspRange(range);

        result.Start.Line.Should().Be(1);
        result.Start.Character.Should().Be(2);
        result.End.Line.Should().Be(3);
        result.End.Character.Should().Be(4);
    }

    [Fact]
    public void ToUri_ShouldCreateFileUri()
    {
        var uri = LspModelMapper.ToUri(@"C:\Users\test\file.cs");

        uri.Should().StartWith("file:///");
        uri.Should().Contain("file.cs");
    }

    [Fact]
    public void ToTextDocumentIdentifier_ShouldUseFilePath()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Path = "file.cs",
            Name = "file.cs",
            FilePath = @"C:\project\file.cs",
        };

        var result = LspModelMapper.ToTextDocumentIdentifier(doc);

        result.Uri.Should().Contain("file.cs");
    }

    [Fact]
    public void ToTextDocumentItem_ShouldIncludeAllFields()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Path = "file.cs",
            Name = "file.cs",
            FilePath = @"C:\project\file.cs",
            Content = "class Foo {}",
            Version = 3,
        };

        var result = LspModelMapper.ToTextDocumentItem(doc, "csharp");

        result.LanguageId.Should().Be("csharp");
        result.Version.Should().Be(3);
        result.Text.Should().Be("class Foo {}");
    }

    // ─── From LSP ───────────────────────────────────────────────────────────

    [Fact]
    public void FromLspPosition_ShouldMapCorrectly()
    {
        var lspPos = new LspPosition { Line = 5, Character = 10 };

        var result = LspModelMapper.FromLspPosition(lspPos);

        result.Line.Should().Be(5);
        result.Column.Should().Be(10);
    }

    [Fact]
    public void FromLspRange_ShouldMapStartAndEnd()
    {
        var lspRange = new LspRange
        {
            Start = new LspPosition { Line = 0, Character = 0 },
            End = new LspPosition { Line = 10, Character = 20 },
        };

        var result = LspModelMapper.FromLspRange(lspRange);

        result.Start.Line.Should().Be(0);
        result.Start.Column.Should().Be(0);
        result.End.Line.Should().Be(10);
        result.End.Column.Should().Be(20);
    }

    [Fact]
    public void FromLspLocation_ShouldMapUriToFilePath()
    {
        var lspLocation = new LspLocation
        {
            Uri = "file:///C:/project/file.cs",
            Range = new LspRange
            {
                Start = new LspPosition { Line = 1, Character = 0 },
                End = new LspPosition { Line = 1, Character = 10 },
            },
        };

        var result = LspModelMapper.FromLspLocation(lspLocation);

        result.FilePath.Should().Contain("file.cs");
        result.Range.Start.Line.Should().Be(1);
    }

    [Fact]
    public void FromLspCompletionItem_ShouldMapAllFields()
    {
        var lspItem = new LspCompletionItem
        {
            Label = "ToString",
            Detail = "string object.ToString()",
            Kind = LspCompletionItemKind.Method,
            InsertText = "ToString()",
            Documentation = new MarkupContent { Value = "Converts to string" },
            InsertTextFormat = InsertTextFormat.PlainText,
        };

        var result = LspModelMapper.FromLspCompletionItem(lspItem);

        result.Label.Should().Be("ToString");
        result.Detail.Should().Be("string object.ToString()");
        result.Kind.Should().Be(CompletionItemKind.Method);
        result.InsertText.Should().Be("ToString()");
        result.Documentation.Should().Be("Converts to string");
        result.IsSnippet.Should().BeFalse();
    }

    [Fact]
    public void FromLspCompletionItem_WithSnippet_ShouldSetIsSnippet()
    {
        var lspItem = new LspCompletionItem
        {
            Label = "for",
            InsertTextFormat = InsertTextFormat.Snippet,
            InsertText = "for (${1:i} = 0; $1 < ${2:count}; $1++) {\n\t$0\n}",
        };

        var result = LspModelMapper.FromLspCompletionItem(lspItem);

        result.IsSnippet.Should().BeTrue();
    }

    [Theory]
    [InlineData(LspCompletionItemKind.Method, CompletionItemKind.Method)]
    [InlineData(LspCompletionItemKind.Function, CompletionItemKind.Function)]
    [InlineData(LspCompletionItemKind.Class, CompletionItemKind.Class)]
    [InlineData(LspCompletionItemKind.Interface, CompletionItemKind.Interface)]
    [InlineData(LspCompletionItemKind.Property, CompletionItemKind.Property)]
    [InlineData(LspCompletionItemKind.Keyword, CompletionItemKind.Keyword)]
    [InlineData(LspCompletionItemKind.Snippet, CompletionItemKind.Snippet)]
    public void FromLspCompletionItem_ShouldMapKindCorrectly(LspCompletionItemKind lspKind, CompletionItemKind expectedKind)
    {
        var lspItem = new LspCompletionItem { Label = "test", Kind = lspKind };

        var result = LspModelMapper.FromLspCompletionItem(lspItem);

        result.Kind.Should().Be(expectedKind);
    }

    [Fact]
    public void FromHoverResult_ShouldMapContentAndRange()
    {
        var hover = new HoverResult
        {
            Contents = new MarkupContent { Value = "```csharp\nint x\n```", Kind = "markdown" },
            Range = new LspRange
            {
                Start = new LspPosition { Line = 5, Character = 0 },
                End = new LspPosition { Line = 5, Character = 3 },
            },
        };

        var result = LspModelMapper.FromHoverResult(hover);

        result.Content.Should().Contain("int x");
        result.Range.Should().NotBeNull();
        result.Range!.Start.Line.Should().Be(5);
    }

    [Fact]
    public void FromHoverResult_WithoutRange_ShouldHaveNullRange()
    {
        var hover = new HoverResult
        {
            Contents = new MarkupContent { Value = "info" },
        };

        var result = LspModelMapper.FromHoverResult(hover);

        result.Range.Should().BeNull();
    }

    [Fact]
    public void FromLspDocumentSymbol_ShouldMapRecursively()
    {
        var lspSymbol = new LspDocumentSymbol
        {
            Name = "MyClass",
            Kind = (int)SymbolKind.Class,
            Range = new LspRange
            {
                Start = new LspPosition { Line = 0, Character = 0 },
                End = new LspPosition { Line = 50, Character = 0 },
            },
            SelectionRange = new LspRange
            {
                Start = new LspPosition { Line = 0, Character = 13 },
                End = new LspPosition { Line = 0, Character = 20 },
            },
            Children =
            [
                new LspDocumentSymbol
                {
                    Name = "MyMethod",
                    Kind = (int)SymbolKind.Method,
                    Range = new LspRange
                    {
                        Start = new LspPosition { Line = 5, Character = 4 },
                        End = new LspPosition { Line = 10, Character = 4 },
                    },
                    SelectionRange = new LspRange
                    {
                        Start = new LspPosition { Line = 5, Character = 16 },
                        End = new LspPosition { Line = 5, Character = 24 },
                    },
                },
            ],
        };

        var result = LspModelMapper.FromLspDocumentSymbol(lspSymbol);

        result.Name.Should().Be("MyClass");
        result.Kind.Should().Be(SymbolKind.Class);
        result.Children.Should().HaveCount(1);
        result.Children[0].Name.Should().Be("MyMethod");
        result.Children[0].Kind.Should().Be(SymbolKind.Method);
    }

    [Fact]
    public void FromLspTextEdit_ShouldMapRangeAndText()
    {
        var lspEdit = new LspTextEdit
        {
            Range = new LspRange
            {
                Start = new LspPosition { Line = 0, Character = 0 },
                End = new LspPosition { Line = 0, Character = 5 },
            },
            NewText = "hello",
        };

        var result = LspModelMapper.FromLspTextEdit(lspEdit);

        result.Range.Start.Column.Should().Be(0);
        result.Range.End.Column.Should().Be(5);
        result.NewText.Should().Be("hello");
    }

    [Theory]
    [InlineData(1, DiagnosticSeverity.Error)]
    [InlineData(2, DiagnosticSeverity.Warning)]
    [InlineData(3, DiagnosticSeverity.Information)]
    [InlineData(4, DiagnosticSeverity.Hint)]
    [InlineData(null, DiagnosticSeverity.Information)]
    public void FromLspDiagnostic_ShouldMapSeverityCorrectly(int? lspSeverity, DiagnosticSeverity expectedSeverity)
    {
        var lspDiag = new LspDiagnostic
        {
            Message = "test error",
            Severity = lspSeverity,
            Range = new LspRange
            {
                Start = new LspPosition { Line = 0, Character = 0 },
                End = new LspPosition { Line = 0, Character = 1 },
            },
            Source = "compiler",
            Code = "CS0001",
        };

        var result = LspModelMapper.FromLspDiagnostic(lspDiag);

        result.Severity.Should().Be(expectedSeverity);
        result.Message.Should().Be("test error");
        result.Source.Should().Be("compiler");
        result.Code.Should().Be("CS0001");
    }

    // ─── Code Action Mapping ────────────────────────────────────────────────

    [Fact]
    public void ToLspDiagnostic_ShouldMapCorrectly()
    {
        var diagnostic = new Diagnostic
        {
            Message = "Unused variable",
            Severity = DiagnosticSeverity.Warning,
            Range = new Range
            {
                Start = new Position { Line = 3, Column = 4 },
                End = new Position { Line = 3, Column = 10 },
            },
            Source = "csharp",
            Code = "CS0219",
        };

        var result = LspModelMapper.ToLspDiagnostic(diagnostic);

        result.Message.Should().Be("Unused variable");
        result.Severity.Should().Be(2); // Warning = 2
        result.Range.Start.Line.Should().Be(3);
        result.Range.Start.Character.Should().Be(4);
        result.Source.Should().Be("csharp");
        result.Code.Should().Be("CS0219");
    }

    [Fact]
    public void FromLspCodeAction_ShouldMapWithEdit()
    {
        var lspAction = new LspCodeAction
        {
            Title = "Add using",
            Kind = "quickfix",
            IsPreferred = true,
            Edit = new LspWorkspaceEdit
            {
                Changes = new Dictionary<string, IReadOnlyList<LspTextEdit>>
                {
                    ["file:///C:/project/test.cs"] =
                    [
                        new LspTextEdit
                        {
                            Range = new LspRange
                            {
                                Start = new LspPosition { Line = 0, Character = 0 },
                                End = new LspPosition { Line = 0, Character = 0 },
                            },
                            NewText = "using System;\n",
                        },
                    ],
                },
            },
        };

        var result = LspModelMapper.FromLspCodeAction(lspAction);

        result.Title.Should().Be("Add using");
        result.Kind.Should().Be("quickfix");
        result.IsPreferred.Should().BeTrue();
        result.Edit.Should().NotBeNull();
        result.Edit!.Changes.Should().ContainKey(@"C:\project\test.cs");
        result.Edit.Changes[@"C:\project\test.cs"].Should().HaveCount(1);
        result.Edit.Changes[@"C:\project\test.cs"][0].NewText.Should().Be("using System;\n");
    }

    [Fact]
    public void FromLspCodeAction_WithoutEdit_ShouldMapCorrectly()
    {
        var lspAction = new LspCodeAction
        {
            Title = "Extract method",
            Kind = "refactor.extract",
        };

        var result = LspModelMapper.FromLspCodeAction(lspAction);

        result.Title.Should().Be("Extract method");
        result.Kind.Should().Be("refactor.extract");
        result.Edit.Should().BeNull();
        result.IsPreferred.Should().BeFalse();
    }

    [Fact]
    public void FromLspWorkspaceEdit_WithMultipleFiles_ShouldMapAll()
    {
        var lspEdit = new LspWorkspaceEdit
        {
            Changes = new Dictionary<string, IReadOnlyList<LspTextEdit>>
            {
                ["file:///C:/project/a.cs"] =
                [
                    new LspTextEdit
                    {
                        Range = new LspRange
                        {
                            Start = new LspPosition { Line = 0, Character = 0 },
                            End = new LspPosition { Line = 0, Character = 5 },
                        },
                        NewText = "hello",
                    },
                ],
                ["file:///C:/project/b.cs"] =
                [
                    new LspTextEdit
                    {
                        Range = new LspRange
                        {
                            Start = new LspPosition { Line = 1, Character = 0 },
                            End = new LspPosition { Line = 1, Character = 3 },
                        },
                        NewText = "world",
                    },
                ],
            },
        };

        var result = LspModelMapper.FromLspWorkspaceEdit(lspEdit);

        result.Changes.Should().HaveCount(2);
        result.Changes.Should().ContainKey(@"C:\project\a.cs");
        result.Changes.Should().ContainKey(@"C:\project\b.cs");
    }
}
