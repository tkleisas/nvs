using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using Serilog;

namespace NVS.Services.Testing;

/// <summary>
/// Discovers tests via <c>dotnet test --list-tests</c> and runs them via
/// <c>dotnet test --logger trx</c>, parsing the TRX file for structured results.
/// </summary>
public sealed class TestExplorerService : ITestExplorerService
{
    private static readonly ILogger Logger = Log.ForContext<TestExplorerService>();

    /// <summary>
    /// Bounds concurrent dotnet CLI operations. Discovery of independent projects
    /// parallelizes well; 2 keeps build/restore contention low when builds do happen.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(2, 2);
    private int _inFlight;

    /// <summary>Whether a discovery or run operation is currently in progress.</summary>
    public bool IsBusy => _inFlight > 0;

    public async Task<IReadOnlyList<TestInfo>> DiscoverTestsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _inFlight);
        try
        {
            var withBuild = new List<string> { "test", projectPath, "--list-tests" };
            var withoutBuild = new List<string> { "test", projectPath, "--list-tests", "--no-build" };

            // Self-hosting (the IDE runs from this solution's output): builds fail on
            // locked binaries, so skip the doomed build attempt and go straight to
            // the already-built binaries; fall back to a full build if they are stale.
            var attempts = IsSelfHostedRun(projectPath)
                ? new[] { withoutBuild, withBuild }
                : new[] { withBuild, withoutBuild };

            var workingDirectory = Path.GetDirectoryName(projectPath);
            List<TestInfo> tests = [];
            foreach (var args in attempts)
            {
                var result = await RunDotnetAsync(args, workingDirectory, cancellationToken).ConfigureAwait(false);
                tests = ParseListedTests(result.StandardOutput, projectPath).ToList();
                if (tests.Count > 0 || result.ExitCode == 0)
                {
                    break;
                }

                Logger.Warning("Test discovery failed for {Project} (exit code {ExitCode}); retrying {Mode}",
                    projectPath, result.ExitCode, args.Contains("--no-build") ? "with build" : "with --no-build");
            }

            Logger.Information("Discovered {Count} tests in {Project}", tests.Count, projectPath);
            return tests;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            _gate.Release();
        }
    }

    public async Task<TestRunSummary> RunTestsAsync(string targetPath, string? filter = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _inFlight);

        var resultsDir = Path.Combine(Path.GetTempPath(), "nvs-test-results", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsDir);

        try
        {
            var args = new List<string>
            {
                "test", targetPath,
                "--logger", "trx;LogFileName=nvs-test-results.trx",
                "--results-directory", resultsDir,
            };
            if (!string.IsNullOrWhiteSpace(filter))
            {
                args.Add("--filter");
                args.Add(filter);
            }

            var stopwatch = Stopwatch.StartNew();
            var withoutBuild = new List<string>(args) { "--no-build" };
            var attempts = IsSelfHostedRun(targetPath)
                ? new[] { withoutBuild, args }
                : new[] { args, withoutBuild };

            var workingDirectory = Path.GetDirectoryName(targetPath);
            ProcessResult result = new(ExitCode: -1, StandardOutput: string.Empty, StandardError: string.Empty);
            IReadOnlyList<TestInfo> tests = [];
            var trxPath = Path.Combine(resultsDir, "nvs-test-results.trx");
            foreach (var attempt in attempts)
            {
                result = await RunDotnetAsync(attempt, workingDirectory, cancellationToken).ConfigureAwait(false);
                tests = await ReadTrxAsync(trxPath, cancellationToken).ConfigureAwait(false);
                if (tests.Count > 0 || result.ExitCode == 0)
                {
                    break;
                }

                Logger.Warning("Test run failed for {Target} (exit code {ExitCode}); retrying {Mode}",
                    targetPath, result.ExitCode, attempt.Contains("--no-build") ? "with build" : "with --no-build");
                TryDeleteFile(trxPath);
            }

            stopwatch.Stop();

            Logger.Information("Test run for {Target}: {Count} result(s), exit code {ExitCode}",
                targetPath, tests.Count, result.ExitCode);

            return new TestRunSummary
            {
                ExitCode = result.ExitCode,
                Tests = tests,
                Duration = stopwatch.Elapsed,
                ErrorOutput = tests.Count == 0 && result.ExitCode != 0 ? result.StandardError : null,
            };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            _gate.Release();
            TryDeleteDirectory(resultsDir);
        }
    }

    /// <summary>
    /// Parses the output of <c>dotnet test --list-tests</c>: every non-empty line
    /// after the "The following Tests are available:" marker is a test FQN.
    /// </summary>
    internal static IEnumerable<TestInfo> ParseListedTests(string output, string? projectPath = null)
    {
        const string marker = "The following Tests are available:";
        var inList = false;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!inList)
            {
                if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                {
                    inList = true;
                }
                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            var nameStart = line.LastIndexOf('.') + 1;
            yield return new TestInfo
            {
                FullyQualifiedName = line,
                DisplayName = line[nameStart..],
                ProjectPath = projectPath,
            };
        }
    }

    /// <summary>
    /// Parses a VSTest TRX file into test results, joining <c>UnitTestResult</c>
    /// entries with their <c>UnitTest</c> definitions for source locations.
    /// </summary>
    internal static IReadOnlyList<TestInfo> ParseTrxResults(string trxContent)
    {
        var doc = XDocument.Parse(trxContent);
        var ns = doc.Root!.Name.Namespace;

        var definitions = doc.Descendants(ns + "UnitTest")
            .Select(ut =>
            {
                var method = ut.Element(ns + "TestMethod");
                return new
                {
                    Id = (string?)ut.Attribute("id") ?? string.Empty,
                    ClassName = (string?)method?.Attribute("className") ?? string.Empty,
                    MethodName = (string?)method?.Attribute("name") ?? string.Empty,
                    CodeFile = (string?)method?.Attribute("codeFile"),
                    Line = int.TryParse((string?)method?.Attribute("lineNumber"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var line)
                        ? line
                        : (int?)null,
                };
            })
            .Where(d => d.Id.Length > 0)
            .ToDictionary(d => d.Id);

        var results = new List<TestInfo>();
        foreach (var result in doc.Descendants(ns + "UnitTestResult"))
        {
            var testId = (string?)result.Attribute("testId") ?? string.Empty;
            definitions.TryGetValue(testId, out var definition);

            var outcome = (string?)result.Attribute("outcome") switch
            {
                "Passed" => TestOutcome.Passed,
                "Failed" => TestOutcome.Failed,
                "NotExecuted" => TestOutcome.Skipped,
                _ => TestOutcome.NotRun,
            };

            var duration = TimeSpan.TryParse((string?)result.Attribute("duration"), CultureInfo.InvariantCulture, out var d)
                ? d
                : (TimeSpan?)null;

            var errorInfo = result.Element(ns + "Output")?.Element(ns + "ErrorInfo");
            var testName = (string?)result.Attribute("testName") ?? string.Empty;
            var fqn = definition is { ClassName.Length: > 0, MethodName.Length: > 0 }
                ? $"{definition.ClassName}.{definition.MethodName}"
                : testName;

            results.Add(new TestInfo
            {
                FullyQualifiedName = fqn,
                DisplayName = testName.Length > 0 ? testName : fqn,
                Outcome = outcome,
                Duration = duration,
                ErrorMessage = errorInfo?.Element(ns + "Message")?.Value,
                StackTrace = errorInfo?.Element(ns + "StackTrace")?.Value,
                CodeFilePath = definition?.CodeFile,
                CodeLine = definition?.Line,
            });
        }

        return results;
    }

    /// <summary>
    /// True when the running IDE executable lives under the target solution/project
    /// directory — meaning builds will fail on locked output files (self-hosting).
    /// </summary>
    internal static bool IsSelfHostedRun(string targetPath)
    {
        var processPath = Environment.ProcessPath;
        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (processPath is null || targetDirectory is null)
        {
            return false;
        }

        var processDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));
        return processDirectory is not null
            && processDirectory.StartsWith(targetDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<TestInfo>> ReadTrxAsync(string trxPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(trxPath))
        {
            return [];
        }

        return ParseTrxResults(await File.ReadAllTextAsync(trxPath, cancellationToken).ConfigureAwait(false));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort; the directory cleanup handles leftovers.
        }
    }

    private static async Task<ProcessResult> RunDotnetAsync(
        IReadOnlyList<string> args, string? workingDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the dotnet process.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to delete temp results directory {Path}", path);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
