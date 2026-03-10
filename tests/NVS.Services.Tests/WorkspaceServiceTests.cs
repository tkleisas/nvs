using FluentAssertions;
using NVS.Services.Workspaces;
using Xunit;

namespace NVS.Services.Tests;

public class WorkspaceServiceTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithNoWorkspace()
    {
        var service = new WorkspaceService();

        service.IsWorkspaceOpen.Should().BeFalse();
        service.CurrentWorkspace.Should().BeNull();
    }

    [Fact]
    public async Task OpenWorkspaceAsync_WithValidPath_ShouldOpenWorkspace()
    {
        var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();

        var workspace = await service.OpenWorkspaceAsync(tempPath);

        workspace.Should().NotBeNull();
        workspace.RootPath.Should().Be(tempPath);
        service.IsWorkspaceOpen.Should().BeTrue();
        service.CurrentWorkspace.Should().Be(workspace);
    }

    [Fact]
    public async Task OpenWorkspaceAsync_ShouldRaiseWorkspaceOpenedEvent()
    {
        var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();
        Core.Models.Workspace? openedWorkspace = null;
        service.WorkspaceOpened += (sender, ws) => openedWorkspace = ws;

        await service.OpenWorkspaceAsync(tempPath);

        openedWorkspace.Should().NotBeNull();
        openedWorkspace!.RootPath.Should().Be(tempPath);
    }

    [Fact]
    public async Task CloseWorkspaceAsync_WhenWorkspaceOpen_ShouldCloseWorkspace()
    {
        var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();
        await service.OpenWorkspaceAsync(tempPath);

        await service.CloseWorkspaceAsync();

        service.IsWorkspaceOpen.Should().BeFalse();
        service.CurrentWorkspace.Should().BeNull();
    }

    [Fact]
    public async Task CloseWorkspaceAsync_ShouldRaiseWorkspaceClosedEvent()
    {
        var service = new WorkspaceService();
        var tempPath = Path.GetTempPath();
        await service.OpenWorkspaceAsync(tempPath);
        Core.Models.Workspace? closedWorkspace = null;
        service.WorkspaceClosed += (sender, ws) => closedWorkspace = ws;

        await service.CloseWorkspaceAsync();

        closedWorkspace.Should().NotBeNull();
        closedWorkspace!.RootPath.Should().Be(tempPath);
    }

    [Fact]
    public async Task OpenWorkspaceAsync_WithInvalidPath_ShouldThrowDirectoryNotFoundException()
    {
        var service = new WorkspaceService();
        var invalidPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var act = async () => await service.OpenWorkspaceAsync(invalidPath);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task CreateWorkspaceAsync_ShouldCreateWorkspace()
    {
        var service = new WorkspaceService();
        var path = "/test/workspace";

        var workspace = await service.CreateWorkspaceAsync(path);

        workspace.Should().NotBeNull();
        workspace.RootPath.Should().Be(path);
        service.IsWorkspaceOpen.Should().BeTrue();
    }
}
