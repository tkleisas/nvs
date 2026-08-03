using System.Diagnostics;
using System.Text.Json;
using NVS.Core.Interfaces;

namespace NVS.Services.Containers;

/// <summary>
/// <see cref="IContainerService"/> over the docker/podman CLI. Detection prefers
/// docker and falls back to podman (CLI-compatible for the operations used).
/// </summary>
public sealed class ContainerService : IContainerService
{
    /// <summary>Process runner seam for tests: (exe, args, workdir, ct) → (exitCode, stdout, stderr).</summary>
    internal Func<string, string, string?, CancellationToken, Task<(int ExitCode, string Stdout, string Stderr)>>? RunnerOverride { get; set; }

    public ContainerEngine Engine { get; private set; } = ContainerEngine.None;

    public async Task<ContainerEngine> RefreshEngineAsync(CancellationToken cancellationToken = default)
    {
        Engine = await DetectEngineAsync(cancellationToken).ConfigureAwait(false);
        return Engine;
    }

    internal static ContainerEngine ChooseEngine(bool dockerAvailable, bool podmanAvailable) =>
        dockerAvailable ? ContainerEngine.Docker
        : podmanAvailable ? ContainerEngine.Podman
        : ContainerEngine.None;

    private async Task<ContainerEngine> DetectEngineAsync(CancellationToken cancellationToken)
    {
        var dockerOk = await ProbeAsync("docker", cancellationToken).ConfigureAwait(false);
        if (dockerOk) return ContainerEngine.Docker;

        var podmanOk = await ProbeAsync("podman", cancellationToken).ConfigureAwait(false);
        return podmanOk ? ContainerEngine.Podman : ContainerEngine.None;
    }

    private async Task<bool> ProbeAsync(string executable, CancellationToken cancellationToken)
    {
        try
        {
            var (exitCode, _, _) = await RunProcessAsync(executable, "version", null, cancellationToken).ConfigureAwait(false);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private string EngineExe => Engine == ContainerEngine.Podman ? "podman" : "docker";

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(
        string args, string? workdir, CancellationToken cancellationToken) =>
        await RunProcessAsync(EngineExe, args, workdir, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(bool includeStopped = true, CancellationToken cancellationToken = default)
    {
        if (Engine == ContainerEngine.None) return [];

        var args = includeStopped ? "ps -a --format \"{{json .}}\"" : "ps --format \"{{json .}}\"";
        var (exitCode, stdout, _) = await RunCliAsync(args, null, cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? ParseContainers(stdout) : [];
    }

    public async Task<IReadOnlyList<ContainerImageInfo>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        if (Engine == ContainerEngine.None) return [];

        var (exitCode, stdout, _) = await RunCliAsync("images --format \"{{json .}}\"", null, cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? ParseImages(stdout) : [];
    }

    public async Task<ContainerOperationResult> BuildImageAsync(
        string dockerfilePath, string imageTag, string contextDirectory, Action<string>? onOutput = null, CancellationToken cancellationToken = default)
    {
        if (Engine == ContainerEngine.None) return ContainerOperationResult.Fail("No container engine detected (docker/podman not installed)");

        var args = $"build -t \"{imageTag}\" -f \"{dockerfilePath}\" \"{contextDirectory}\"";
        var (exitCode, _, stderr) = await RunProcessAsync(EngineExe, args, contextDirectory, cancellationToken, onOutput).ConfigureAwait(false);
        return exitCode == 0
            ? ContainerOperationResult.Ok($"Image built: {imageTag}")
            : ContainerOperationResult.Fail($"docker build failed (exit {exitCode}): {TrimForMessage(stderr)}");
    }

    public async Task<ContainerOperationResult> RunContainerAsync(
        string image, string? name = null, IReadOnlyList<string>? portMappings = null, CancellationToken cancellationToken = default)
    {
        if (Engine == ContainerEngine.None) return ContainerOperationResult.Fail("No container engine detected (docker/podman not installed)");

        var args = "run -d";
        if (!string.IsNullOrWhiteSpace(name)) args += $" --name \"{name}\"";
        foreach (var mapping in portMappings ?? [])
        {
            args += $" -p \"{mapping}\"";
        }
        args += $" \"{image}\"";

        var (exitCode, stdout, stderr) = await RunCliAsync(args, null, cancellationToken).ConfigureAwait(false);
        return exitCode == 0
            ? ContainerOperationResult.Ok($"Container started: {stdout.Trim()}")
            : ContainerOperationResult.Fail($"docker run failed (exit {exitCode}): {TrimForMessage(stderr)}");
    }

    public Task<ContainerOperationResult> StartContainerAsync(string containerId, CancellationToken cancellationToken = default) =>
        SimpleCliActionAsync($"start \"{containerId}\"", "start", cancellationToken);

    public Task<ContainerOperationResult> StopContainerAsync(string containerId, CancellationToken cancellationToken = default) =>
        SimpleCliActionAsync($"stop \"{containerId}\"", "stop", cancellationToken);

    public Task<ContainerOperationResult> RemoveContainerAsync(string containerId, bool force = false, CancellationToken cancellationToken = default) =>
        SimpleCliActionAsync($"rm {(force ? "-f " : "")}\"{containerId}\"", "remove", cancellationToken);

    public async Task<string> GetContainerLogsAsync(string containerId, int tailLines = 200, CancellationToken cancellationToken = default)
    {
        if (Engine == ContainerEngine.None) return "(no container engine detected)";

        var (exitCode, stdout, stderr) = await RunCliAsync($"logs --tail {tailLines} \"{containerId}\"", null, cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? stdout : $"logs failed (exit {exitCode}): {stderr}";
    }

    public Task<ContainerOperationResult> ComposeUpAsync(string composeFilePath, Action<string>? onOutput = null, CancellationToken cancellationToken = default) =>
        ComposeAsync(composeFilePath, "up -d --build", onOutput, cancellationToken);

    public Task<ContainerOperationResult> ComposeDownAsync(string composeFilePath, Action<string>? onOutput = null, CancellationToken cancellationToken = default) =>
        ComposeAsync(composeFilePath, "down", onOutput, cancellationToken);

    private async Task<ContainerOperationResult> ComposeAsync(string composeFilePath, string action, Action<string>? onOutput, CancellationToken cancellationToken)
    {
        if (Engine == ContainerEngine.None) return ContainerOperationResult.Fail("No container engine detected (docker/podman not installed)");

        var workdir = Path.GetDirectoryName(composeFilePath);
        var (exitCode, _, stderr) = await RunProcessAsync(EngineExe, $"compose -f \"{composeFilePath}\" {action}", workdir, cancellationToken, onOutput).ConfigureAwait(false);
        return exitCode == 0
            ? ContainerOperationResult.Ok($"compose {action.Split(' ')[0]} finished")
            : ContainerOperationResult.Fail($"compose failed (exit {exitCode}): {TrimForMessage(stderr)}");
    }

    private async Task<ContainerOperationResult> SimpleCliActionAsync(string args, string verb, CancellationToken cancellationToken)
    {
        if (Engine == ContainerEngine.None) return ContainerOperationResult.Fail("No container engine detected (docker/podman not installed)");

        var (exitCode, _, stderr) = await RunCliAsync(args, null, cancellationToken).ConfigureAwait(false);
        return exitCode == 0
            ? ContainerOperationResult.Ok($"Container {verb} succeeded")
            : ContainerOperationResult.Fail($"Container {verb} failed (exit {exitCode}): {TrimForMessage(stderr)}");
    }

    internal static IReadOnlyList<ContainerInfo> ParseContainers(string jsonLines)
    {
        var result = new List<ContainerInfo>();
        foreach (var line in jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                result.Add(new ContainerInfo
                {
                    Id = GetString(root, "ID"),
                    Name = GetString(root, "Names"),
                    Image = GetString(root, "Image"),
                    Status = GetString(root, "Status"),
                    State = GetString(root, "State"),
                    Ports = GetString(root, "Ports"),
                });
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }
        return result;
    }

    internal static IReadOnlyList<ContainerImageInfo> ParseImages(string jsonLines)
    {
        var result = new List<ContainerImageInfo>();
        foreach (var line in jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                result.Add(new ContainerImageInfo
                {
                    Id = GetString(root, "ID"),
                    Repository = GetString(root, "Repository"),
                    Tag = GetString(root, "Tag"),
                    Size = GetString(root, "Size"),
                });
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }
        return result;
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string TrimForMessage(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > 300 ? trimmed[..300] + "…" : trimmed;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string executable, string arguments, string? workingDirectory, CancellationToken cancellationToken, Action<string>? onOutput = null)
    {
        if (RunnerOverride is not null)
        {
            return await RunnerOverride(executable, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        var psi = new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
                onOutput?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
                onOutput?.Invoke(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
