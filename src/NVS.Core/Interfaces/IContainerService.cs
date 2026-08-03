namespace NVS.Core.Interfaces;

/// <summary>The detected container engine on the host.</summary>
public enum ContainerEngine
{
    None,
    Docker,
    Podman,
}

/// <summary>A container as reported by the engine's ps command.</summary>
public sealed record ContainerInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Image { get; init; }
    public required string Status { get; init; }
    public required string State { get; init; }
    public string Ports { get; init; } = string.Empty;
    public bool IsRunning => State.Equals("running", StringComparison.OrdinalIgnoreCase);
}

/// <summary>An image as reported by the engine's images command.</summary>
public sealed record ContainerImageInfo
{
    public required string Id { get; init; }
    public required string Repository { get; init; }
    public required string Tag { get; init; }
    public required string Size { get; init; }
    public string DisplayName => Tag is "<none>" or "" ? Repository : $"{Repository}:{Tag}";
}

/// <summary>Result of a container CLI operation (build, run, start, stop, compose).</summary>
public sealed record ContainerOperationResult
{
    public required bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public static ContainerOperationResult Ok(string message = "") => new() { Success = true, Message = message };
    public static ContainerOperationResult Fail(string message) => new() { Success = false, Message = message };
}

/// <summary>
/// Docker/Podman integration: engine detection plus build/run/manage operations
/// over the engine CLI (the two are command-line compatible for everything used here).
/// </summary>
public interface IContainerService
{
    /// <summary>The detected engine (None when neither docker nor podman is installed).</summary>
    ContainerEngine Engine { get; }

    /// <summary>Re-detects the engine. Returns the detected engine.</summary>
    Task<ContainerEngine> RefreshEngineAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(bool includeStopped = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContainerImageInfo>> ListImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Builds an image from a Dockerfile, streaming output lines to <paramref name="onOutput"/>.</summary>
    Task<ContainerOperationResult> BuildImageAsync(string dockerfilePath, string imageTag, string contextDirectory, Action<string>? onOutput = null, CancellationToken cancellationToken = default);

    /// <summary>Runs an image as a new container (detached).</summary>
    Task<ContainerOperationResult> RunContainerAsync(string image, string? name = null, IReadOnlyList<string>? portMappings = null, CancellationToken cancellationToken = default);

    Task<ContainerOperationResult> StartContainerAsync(string containerId, CancellationToken cancellationToken = default);
    Task<ContainerOperationResult> StopContainerAsync(string containerId, CancellationToken cancellationToken = default);
    Task<ContainerOperationResult> RemoveContainerAsync(string containerId, bool force = false, CancellationToken cancellationToken = default);

    Task<string> GetContainerLogsAsync(string containerId, int tailLines = 200, CancellationToken cancellationToken = default);

    /// <summary>Runs `compose up -d --build` for a compose file, streaming output.</summary>
    Task<ContainerOperationResult> ComposeUpAsync(string composeFilePath, Action<string>? onOutput = null, CancellationToken cancellationToken = default);

    /// <summary>Runs `compose down` for a compose file, streaming output.</summary>
    Task<ContainerOperationResult> ComposeDownAsync(string composeFilePath, Action<string>? onOutput = null, CancellationToken cancellationToken = default);
}
