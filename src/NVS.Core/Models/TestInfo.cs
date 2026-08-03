namespace NVS.Core.Models;

public enum TestOutcome
{
    NotRun,
    Running,
    Passed,
    Failed,
    Skipped,
}

/// <summary>A single test case discovered in, or reported from, a test project.</summary>
public sealed record TestInfo
{
    /// <summary>Fully qualified name: Namespace.Class.Method (no arguments).</summary>
    public required string FullyQualifiedName { get; init; }

    /// <summary>Human-readable name; for parameterized tests includes arguments.</summary>
    public required string DisplayName { get; init; }

    public string? ProjectPath { get; init; }
    public TestOutcome Outcome { get; init; } = TestOutcome.NotRun;
    public TimeSpan? Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public string? StackTrace { get; init; }

    /// <summary>Source file of the test method (from TRX), when available.</summary>
    public string? CodeFilePath { get; init; }

    /// <summary>1-based source line of the test method (from TRX), when available.</summary>
    public int? CodeLine { get; init; }
}

/// <summary>Result of a test run: parsed TRX results plus process information.</summary>
public sealed record TestRunSummary
{
    public required int ExitCode { get; init; }
    public required IReadOnlyList<TestInfo> Tests { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>Process stderr, set when no TRX results could be produced (e.g. build failure).</summary>
    public string? ErrorOutput { get; init; }
}
