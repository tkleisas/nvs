using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Core.Models.Settings;
using NVS.Services.Lsp;
using NVS.Services.Roslyn;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Services.Tests;

/// <summary>End-to-end: SymbolsToolViewModel + real LspSessionManager + real Roslyn on a loaded workspace.</summary>
public class SymbolsToolRealChainTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly RoslynCompletionService _roslyn = new();
    private readonly LspSessionManager _manager;
    private readonly MainViewModel _main;

    public SymbolsToolRealChainTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nvs-symtool-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _manager = new LspSessionManager(Substitute.For<ILspClientFactory>(), _roslyn);

        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());
        _main = new MainViewModel(
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

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync();
        await _roslyn.DisposeAsync();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Refresh_RealChain_PopulatesOutline()
    {
        var csproj = Path.Combine(_tempDir, "Probe.csproj");
        await File.WriteAllTextAsync(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var cs = Path.Combine(_tempDir, "Widget.cs");
        await File.WriteAllTextAsync(cs, "namespace Probe;\n\npublic class Widget { }\n");
        await _roslyn.LoadWorkspaceAsync(csproj);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            Path = cs,
            Name = "Widget.cs",
            FilePath = cs,
            Language = Language.CSharp,
            Content = await File.ReadAllTextAsync(cs),
        };
        _main.Editor!.ActiveDocument = new DocumentViewModel(document);

        var vm = new SymbolsToolViewModel(_main, _manager);
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Symbols.Should().NotBeEmpty(
            $"expected symbols from the real chain; status was '{vm.StatusText}'");
        vm.Symbols.Should().Contain(s => s.Name == "Probe");
    }
}
