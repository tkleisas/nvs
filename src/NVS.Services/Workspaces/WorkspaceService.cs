using NVS.Core.Interfaces;

namespace NVS.Services.Workspaces;

public sealed class WorkspaceService : IWorkspaceService
{
    private Core.Models.Workspace? _currentWorkspace;

    public Core.Models.Workspace? CurrentWorkspace => _currentWorkspace;
    public bool IsWorkspaceOpen => _currentWorkspace != null;

    public event EventHandler<Core.Models.Workspace>? WorkspaceOpened;
    public event EventHandler<Core.Models.Workspace>? WorkspaceClosed;

    public Task<Core.Models.Workspace> CreateWorkspaceAsync(string path, CancellationToken cancellationToken = default)
    {
        var workspace = new Core.Models.Workspace
        {
            Id = Guid.NewGuid(),
            Name = System.IO.Path.GetFileName(path) ?? "Workspace",
            RootPath = path,
            Folders = [],
            CreatedAt = DateTimeOffset.UtcNow,
            LastOpenedAt = DateTimeOffset.UtcNow
        };

        _currentWorkspace = workspace;
        WorkspaceOpened?.Invoke(this, workspace);

        return Task.FromResult(workspace);
    }

    public Task<Core.Models.Workspace> OpenWorkspaceAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Workspace directory not found: {path}");
        }

        var workspace = new Core.Models.Workspace
        {
            Id = Guid.NewGuid(),
            Name = System.IO.Path.GetFileName(path) ?? "Workspace",
            RootPath = path,
            Folders = DiscoverFolders(path),
            CreatedAt = DateTimeOffset.UtcNow,
            LastOpenedAt = DateTimeOffset.UtcNow
        };

        _currentWorkspace = workspace;
        WorkspaceOpened?.Invoke(this, workspace);

        return Task.FromResult(workspace);
    }

    public Task CloseWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        if (_currentWorkspace != null)
        {
            var closedWorkspace = _currentWorkspace;
            _currentWorkspace = null;
            WorkspaceClosed?.Invoke(this, closedWorkspace);
        }

        return Task.CompletedTask;
    }

    public Task SaveWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private static IReadOnlyList<string> DiscoverFolders(string rootPath)
    {
        try
        {
            var directories = Directory.GetDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !IsHiddenOrExcluded(System.IO.Path.GetFileName(d)))
                .ToList();

            return directories;
        }
        catch
        {
            return [];
        }
    }

    private static bool IsHiddenOrExcluded(string directoryName)
    {
        return directoryName.StartsWith('.') ||
               directoryName is "node_modules" or "bin" or "obj" or "packages";
    }
}
