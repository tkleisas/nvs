using NVS.Core.Enums;
using NVS.Core.Models;
using NVS.Services.Lsp;
using NVS.Services.Roslyn;

namespace NVS.Services.Tests;

/// <summary>End-to-end probe: LspSessionManager + real Roslyn service on a loaded workspace.</summary>
public class LspSessionManagerRoslynSymbolsProbeTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly RoslynCompletionService _roslyn = new();
    private readonly LspSessionManager _manager;

    public LspSessionManagerRoslynSymbolsProbeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nvs-lsp-probe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _manager = new LspSessionManager(
            Substitute.For<NVS.Services.Lsp.ILspClientFactory>(),
            _roslyn);
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
    public async Task GetDocumentSymbolsAsync_LoadedWorkspace_UsesRoslynNotLspClient()
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

        var symbols = await _manager.GetDocumentSymbolsAsync(document);

        symbols.Should().NotBeEmpty("Roslyn has the document and the workspace is loaded");
        symbols.Should().Contain(s => s.Name == "Probe");
    }
}
