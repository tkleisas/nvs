using NVS.Core.Models;

namespace NVS.Core.Interfaces;

public interface IWorkspaceService
{
    Workspace? CurrentWorkspace { get; }
    bool IsWorkspaceOpen { get; }
    
    Task<Workspace> CreateWorkspaceAsync(string path, CancellationToken cancellationToken = default);
    Task<Workspace> OpenWorkspaceAsync(string path, CancellationToken cancellationToken = default);
    Task CloseWorkspaceAsync(CancellationToken cancellationToken = default);
    Task SaveWorkspaceAsync(CancellationToken cancellationToken = default);
    
    event EventHandler<Workspace>? WorkspaceOpened;
    event EventHandler<Workspace>? WorkspaceClosed;
}
