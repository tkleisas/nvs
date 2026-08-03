using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.ViewModels.Dock;
using Range = NVS.Core.Models.Range;

namespace NVS.Tests;

public class SymbolsToolViewModelTests
{
    private static ILspSessionManager LspWithSymbols(IReadOnlyList<DocumentSymbol> symbols)
    {
        var lsp = Substitute.For<ILspSessionManager>();
        lsp.GetDocumentSymbolsAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(symbols);
        return lsp;
    }

    private static MainViewModel CreateMain()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());
        return new MainViewModel(
            Substitute.For<IWorkspaceService>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IFileSystemService>(),
            new EditorViewModel(Substitute.For<IEditorService>(), Substitute.For<IFileSystemService>()),
            Substitute.For<IGitService>(),
            Substitute.For<ITerminalService>(),
            settings,
            Substitute.For<ISolutionService>(),
            Substitute.For<IBuildService>());
    }

    private static DocumentSymbol Method(string name, int line, params DocumentSymbol[] children) => new()
    {
        Name = name,
        Kind = SymbolKind.Method,
        Range = new Range { Start = new Position { Line = line, Column = 0 }, End = new Position { Line = line + 3, Column = 1 } },
        SelectionRange = new Range { Start = new Position { Line = line, Column = 6 }, End = new Position { Line = line, Column = 12 } },
        Children = children,
    };

    [Fact]
    public async Task Refresh_MapsSymbolsWithOneBasedLines()
    {
        var main = CreateMain();
        main.Editor!.NewFile();
        var lsp = LspWithSymbols(
        [
            Method("Run", 9,
                new DocumentSymbol
                {
                    Name = "helper",
                    Kind = SymbolKind.Method,
                    SelectionRange = new Range { Start = new Position { Line = 14, Column = 4 }, End = new Position { Line = 14, Column = 10 } },
                }),
        ]);
        var vm = new SymbolsToolViewModel(main, lsp);

        await vm.RefreshCommand.ExecuteAsync(null);

        var root = vm.Symbols.Should().ContainSingle().Subject;
        root.Name.Should().Be("Run");
        root.Line.Should().Be(10); // 0-based 9 → 1-based 10
        root.Children.Should().ContainSingle().Which.Line.Should().Be(15);
        vm.StatusText.Should().Contain("2 symbol(s)");
    }

    [Fact]
    public async Task NavigateToSymbol_SetsCursorLine()
    {
        var main = CreateMain();
        main.Editor!.NewFile();
        var vm = new SymbolsToolViewModel(main, LspWithSymbols([Method("Run", 41)]));
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.NavigateToSymbolCommand.Execute(vm.Symbols[0]);

        main.Editor.ActiveDocument!.CursorLine.Should().Be(42);
    }

    [Fact]
    public async Task Refresh_NoDocument_ShowsMessage()
    {
        var main = CreateMain();
        var vm = new SymbolsToolViewModel(main, Substitute.For<ILspSessionManager>());

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.StatusText.Should().Be("No document open");
    }
}
