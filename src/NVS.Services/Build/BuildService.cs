using System.Diagnostics;
using System.Text.RegularExpressions;
using NVS.Core.Interfaces;

namespace NVS.Services.Build;

public sealed partial class BuildService : IBuildService, IDisposable
{
    private Process? _activeProcess;
    private readonly object _lock = new();
    private readonly List<BuildError> _errors = [];
    private readonly List<BuildWarning> _warnings = [];

    public bool IsBuilding { get; private set; }
    public BuildTask? CurrentTask { get; private set; }

    public event EventHandler<BuildOutputEventArgs>? OutputReceived;
    public event EventHandler<BuildResult>? BuildCompleted;

    public async Task<BuildResult> RunTaskAsync(BuildTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (IsBuilding)
            throw new InvalidOperationException("A build is already in progress.");

        IsBuilding = true;
        CurrentTask = task;
        _errors.Clear();
        _warnings.Clear();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = task.Command,
                WorkingDirectory = task.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in task.Args)
                startInfo.ArgumentList.Add(arg);

            foreach (var (key, value) in task.Environment)
                startInfo.Environment[key] = value;

            using var process = new Process { StartInfo = startInfo };
            lock (_lock) { _activeProcess = process; }

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                ParseOutputLine(e.Data, isError: false);
                OutputReceived?.Invoke(this, new BuildOutputEventArgs { Output = e.Data, IsError = false });
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                ParseOutputLine(e.Data, isError: true);
                OutputReceived?.Invoke(this, new BuildOutputEventArgs { Output = e.Data, IsError = true });
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var result = new BuildResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Duration = stopwatch.Elapsed,
                Errors = [.. _errors],
                Warnings = [.. _warnings]
            };

            BuildCompleted?.Invoke(this, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            KillActiveProcess();

            var result = new BuildResult
            {
                Success = false,
                ExitCode = -1,
                Duration = stopwatch.Elapsed,
                Errors = [new BuildError { Message = "Build was cancelled." }],
                Warnings = [.. _warnings]
            };

            BuildCompleted?.Invoke(this, result);
            return result;
        }
        finally
        {
            lock (_lock) { _activeProcess = null; }
            IsBuilding = false;
            CurrentTask = null;
        }
    }

    public Task<BuildResult> RunDefaultTaskAsync(CancellationToken cancellationToken = default)
    {
        var task = new BuildTask
        {
            Name = "Build",
            Command = "dotnet",
            Args = ["build", "--nologo"]
        };

        return RunTaskAsync(task, cancellationToken);
    }

    public Task CancelAsync()
    {
        KillActiveProcess();
        return Task.CompletedTask;
    }

    internal void ParseOutputLine(string line, bool isError)
    {
        var errorMatch = MsBuildErrorRegex().Match(line);
        if (errorMatch.Success)
        {
            var filePath = errorMatch.Groups["file"].Value;
            var lineNum = int.TryParse(errorMatch.Groups["line"].Value, out var l) ? l : (int?)null;
            var col = int.TryParse(errorMatch.Groups["col"].Value, out var c) ? c : (int?)null;
            var severity = errorMatch.Groups["severity"].Value;
            var message = errorMatch.Groups["message"].Value.Trim();

            if (severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                _errors.Add(new BuildError
                {
                    Message = message,
                    FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath,
                    Line = lineNum,
                    Column = col
                });
            }
            else if (severity.Equals("warning", StringComparison.OrdinalIgnoreCase))
            {
                _warnings.Add(new BuildWarning
                {
                    Message = message,
                    FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath,
                    Line = lineNum,
                    Column = col
                });
            }
        }
    }

    private void KillActiveProcess()
    {
        lock (_lock)
        {
            if (_activeProcess is not null)
            {
                try
                {
                    if (!_activeProcess.HasExited)
                        _activeProcess.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
            }
        }
    }

    public void Dispose()
    {
        KillActiveProcess();
    }

    // MSBuild error format: path(line,col): error/warning CODE: message
    [GeneratedRegex(@"(?<file>[^(]+)\((?<line>\d+),(?<col>\d+)\):\s*(?<severity>error|warning)\s+(?<message>.+)", RegexOptions.IgnoreCase)]
    private static partial Regex MsBuildErrorRegex();
}
