using NVS.Services.Roslyn;

namespace NVS.Services.Tests;

/// <summary>Probe for the empty-outline investigation: symbols must come back for a loaded workspace document.</summary>
public class RoslynDocumentSymbolsProbeTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly RoslynCompletionService _service = new();

    public RoslynDocumentSymbolsProbeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nvs-roslyn-probe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public async ValueTask DisposeAsync()
    {
        await _service.DisposeAsync();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetDocumentSymbolsAsync_LoadedWorkspace_ReturnsSymbols()
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
        await File.WriteAllTextAsync(cs, """
            namespace Probe;

            public class Widget
            {
                public int Count { get; set; }

                public void Spin() { }
            }
            """);

        await _service.LoadWorkspaceAsync(csproj);

        var mapField = typeof(RoslynCompletionService).GetField("_documentMap",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var map = (System.Collections.Concurrent.ConcurrentDictionary<string, Microsoft.CodeAnalysis.DocumentId>)mapField.GetValue(_service)!;
        var solutionField = typeof(RoslynCompletionService).GetField("_solution",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var solution = (Microsoft.CodeAnalysis.Solution?)solutionField.GetValue(_service);

        var projectCount = solution?.Projects.Count() ?? -1;
        var docCount = map.Count;

        var symbols = await _service.GetDocumentSymbolsAsync(cs);

        symbols.Should().NotBeEmpty("the document is part of the loaded project");
        var ns = symbols.Should().ContainSingle(s => s.Name == "Probe").Subject;
        ns.Children.Select(c => c.Name).Should().Contain("Widget");
    }
}
