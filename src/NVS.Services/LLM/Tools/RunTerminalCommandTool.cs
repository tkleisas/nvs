using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Executes a shell command and returns stdout/stderr capped at 8KB.
/// </summary>
public sealed class RunTerminalCommandTool : IAgentTool
{
    private const int MaxOutputBytes = 8192;

    private readonly Func<string> _getWorkspacePath;

    public string Name => "run_terminal";
    public string Description => "Execute a shell command in the workspace directory. Returns stdout and stderr (max 8KB). Use for arbitrary commands like grep, find, curl, etc.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "command": {
                    "type": "string",
                    "description": "The command to execute (e.g. 'ls -la', 'grep -rn TODO src/', 'cat package.json')"
                },
                "timeout_seconds": {
                    "type": "integer",
                    "description": "Maximum seconds to wait. Defaults to 30."
                }
            },
            "required": ["command"]
        }
        """);

    public RunTerminalCommandTool(Func<string> getWorkspacePath)
    {
        _getWorkspacePath = getWorkspacePath;
    }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        var command = args.GetProperty("command").GetString() ?? throw new ArgumentException("command is required");
        var timeout = args.TryGetProperty("timeout_seconds", out var t) ? t.GetInt32() : 30;

        var workspace = _getWorkspacePath();
        var isWindows = OperatingSystem.IsWindows();

        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/sh",
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeout));

            var stdoutTask = ReadCappedAsync(process.StandardOutput, cts.Token);
            var stderrTask = ReadCappedAsync(process.StandardError, cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return JsonSerializer.Serialize(new
            {
                exit_code = process.ExitCode,
                stdout,
                stderr,
                truncated = stdout.Length >= MaxOutputBytes || stderr.Length >= MaxOutputBytes
            });
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = $"Command timed out after {timeout}s" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private static async Task<string> ReadCappedAsync(System.IO.StreamReader reader, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        int totalRead = 0;

        while (totalRead < MaxOutputBytes)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxOutputBytes - totalRead)), ct);
            if (read == 0) break;
            sb.Append(buffer, 0, read);
            totalRead += read;
        }

        return sb.ToString();
    }
}
