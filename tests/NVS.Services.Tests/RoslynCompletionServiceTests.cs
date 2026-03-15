using NVS.Core.Interfaces;
using NVS.Services.Roslyn;
using CompletionItem = NVS.Core.Interfaces.CompletionItem;

namespace NVS.Services.Tests;

public sealed class RoslynCompletionServiceTests : IAsyncDisposable
{
    private readonly RoslynCompletionService _service;

    public RoslynCompletionServiceTests()
    {
        _service = new RoslynCompletionService();
    }

    public async ValueTask DisposeAsync()
    {
        await _service.DisposeAsync();
    }

    // ─── Initial State ──────────────────────────────────────────────────────

    [Fact]
    public void IsWorkspaceLoaded_Initially_ShouldBeFalse()
    {
        _service.IsWorkspaceLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task GetCompletionsAsync_WithNoWorkspace_ShouldReturnEmpty()
    {
        var results = await _service.GetCompletionsAsync(@"C:\test\Program.cs", 0, 0);
        results.Should().BeEmpty();
    }

    [Fact]
    public void UpdateDocumentContent_WithNoWorkspace_ShouldNotThrow()
    {
        var act = () => _service.UpdateDocumentContent(@"C:\test\Program.cs", "// content");
        act.Should().NotThrow();
    }

    [Fact]
    public void UnloadWorkspace_WithNoWorkspace_ShouldNotThrow()
    {
        var act = () => _service.UnloadWorkspace();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotent()
    {
        await _service.DisposeAsync();
        await _service.DisposeAsync(); // second call should not throw
    }

    [Fact]
    public async Task LoadWorkspaceAsync_WithUnsupportedExtension_ShouldNotThrow()
    {
        // .txt is not a supported workspace file — should log a warning but not throw
        await _service.LoadWorkspaceAsync(@"C:\test\readme.txt");
        _service.IsWorkspaceLoaded.Should().BeFalse();
    }

    // ─── Workspace Loading (Integration) ────────────────────────────────────

    [Fact]
    public async Task LoadWorkspaceAsync_WithInvalidPath_ShouldThrow()
    {
        // A non-existent .csproj should throw from Roslyn/MSBuild
        var act = () => _service.LoadWorkspaceAsync(@"C:\nonexistent\fake.csproj");
        await act.Should().ThrowAsync<Exception>();
    }
}
