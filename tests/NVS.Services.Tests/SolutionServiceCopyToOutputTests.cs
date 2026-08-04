using System.Xml.Linq;
using NVS.Core.Models;
using NVS.Services.Solution;

namespace NVS.Services.Tests;

public sealed class SolutionServiceCopyToOutputTests : IDisposable
{
    private const string ProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nvs-csproj-test-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _projectPath;
    private readonly SolutionService _service = new();

    public SolutionServiceCopyToOutputTests()
    {
        Directory.CreateDirectory(_dir);
        _projectPath = Path.Combine(_dir, "App.csproj");
        File.WriteAllText(_projectPath, ProjectXml);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Always_AddsNoneUpdateItemWithMetadata()
    {
        await _service.SetCopyToOutputDirectoryAsync(_projectPath, "appsettings.json", CopyToOutputMode.Always);

        var item = FindItem();
        item.Should().NotBeNull();
        item!.Attribute("Update")!.Value.Should().Be("appsettings.json");
        item.Element("CopyToOutputDirectory")!.Value.Should().Be("Always");
    }

    [Fact]
    public async Task PreserveNewest_WritesCopyIfNewerValue()
    {
        await _service.SetCopyToOutputDirectoryAsync(_projectPath, "appsettings.json", CopyToOutputMode.PreserveNewest);

        FindItem()!.Element("CopyToOutputDirectory")!.Value.Should().Be("PreserveNewest");
    }

    [Fact]
    public async Task ChangingMode_UpdatesInPlaceWithoutDuplicates()
    {
        await _service.SetCopyToOutputDirectoryAsync(_projectPath, "appsettings.json", CopyToOutputMode.Always);
        await _service.SetCopyToOutputDirectoryAsync(_projectPath, "appsettings.json", CopyToOutputMode.PreserveNewest);

        var doc = XDocument.Load(_projectPath);
        var items = doc.Descendants("None")
            .Where(e => e.Attribute("Update")?.Value == "appsettings.json")
            .ToList();
        items.Should().HaveCount(1);
        items[0].Element("CopyToOutputDirectory")!.Value.Should().Be("PreserveNewest");
    }

    [Fact]
    public async Task Never_RemovesMetadata()
    {
        await _service.SetCopyToOutputDirectoryAsync(_projectPath, "appsettings.json", CopyToOutputMode.Always);
        await _service.SetCopyToOutputDirectoryAsync(_projectPath, "appsettings.json", CopyToOutputMode.Never);

        var item = FindItem();
        item.Should().NotBeNull();
        item!.Element("CopyToOutputDirectory").Should().BeNull();
    }

    [Fact]
    public async Task ForwardSlashes_AreNormalized()
    {
        await _service.SetCopyToOutputDirectoryAsync(_projectPath, "config/appsettings.json", CopyToOutputMode.Always);

        FindItem()!.Attribute("Update")!.Value.Should().Be(@"config\appsettings.json");
    }

    [Fact]
    public async Task ProjectStillParsesAndBuilds()
    {
        await _service.SetCopyToOutputDirectoryAsync(_projectPath, "appsettings.json", CopyToOutputMode.Always);

        var doc = XDocument.Load(_projectPath);
        doc.Root!.Name.LocalName.Should().Be("Project");
        doc.Root.Attribute("Sdk")!.Value.Should().Be("Microsoft.NET.Sdk");
    }

    private XElement? FindItem()
    {
        var doc = XDocument.Load(_projectPath);
        return doc.Descendants("None")
            .FirstOrDefault(e => string.Equals(
                e.Attribute("Update")?.Value ?? e.Attribute("Include")?.Value,
                @"appsettings.json", StringComparison.OrdinalIgnoreCase)
                || (e.Attribute("Update")?.Value ?? "").EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase));
    }
}
