using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.ViewModels.Dock;

public enum TestNodeKind
{
    Project,
    Namespace,
    Class,
    Test,
}

/// <summary>One node in the Test Explorer tree (project → namespace → class → test).</summary>
public partial class TestNodeViewModel : ObservableObject
{
    public required string Name { get; init; }
    public required TestNodeKind Kind { get; init; }
    public ObservableCollection<TestNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon))]
    private TestOutcome _outcome = TestOutcome.NotRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    private TimeSpan? _duration;

    /// <summary>Latest info for leaf test nodes (run details, source location).</summary>
    public TestInfo? Test { get; set; }

    /// <summary>Project file path, set on project nodes.</summary>
    public string? ProjectPath { get; init; }

    public string Icon => Outcome switch
    {
        TestOutcome.Passed => "✓",
        TestOutcome.Failed => "✗",
        TestOutcome.Skipped => "⊘",
        TestOutcome.Running => "▶",
        _ => "○",
    };

    public string DurationText => Duration is { } d ? $"{d.TotalMilliseconds:F0} ms" : string.Empty;
}

/// <summary>
/// Test Explorer: discovers tests in the loaded solution's test projects, runs them
/// (all / failed / selected) via dotnet test, and navigates to failing test sources.
/// </summary>
public partial class TestExplorerToolViewModel : Tool
{
    private readonly MainViewModel _main;
    private readonly ITestExplorerService? _testService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Discover tests to begin";

    [ObservableProperty]
    private TestNodeViewModel? _selectedNode;

    public ObservableCollection<TestNodeViewModel> Projects { get; } = [];

    public TestExplorerToolViewModel(MainViewModel main, ITestExplorerService? testService = null)
    {
        _main = main;
        _testService = testService;
        Id = "TestExplorer";
        Title = "🧪 Tests";
        CanClose = true;
        CanPin = true;

        if (testService is not null)
        {
            main.SolutionService.SolutionLoaded += (_, _) => _ = DiscoverSafeAsync();
            main.SolutionService.SolutionClosed += (_, _) =>
            {
                Projects.Clear();
                StatusText = "No solution loaded";
            };
        }
    }

    /// <summary>Details of the selected test: name, duration, error message and stack trace.</summary>
    public string DetailsText
    {
        get
        {
            if (SelectedNode?.Test is not { } test)
            {
                return string.Empty;
            }

            var parts = new List<string> { test.DisplayName };
            if (test.Duration is { } duration)
            {
                parts.Add($"Duration: {duration.TotalMilliseconds:F0} ms");
            }
            if (!string.IsNullOrEmpty(test.ErrorMessage))
            {
                parts.Add(string.Empty);
                parts.Add(test.ErrorMessage);
            }
            if (!string.IsNullOrEmpty(test.StackTrace))
            {
                parts.Add(string.Empty);
                parts.Add(test.StackTrace);
            }
            return string.Join('\n', parts);
        }
    }

    partial void OnSelectedNodeChanged(TestNodeViewModel? value) =>
        OnPropertyChanged(nameof(DetailsText));

    [RelayCommand]
    private async Task DiscoverAsync(CancellationToken ct)
    {
        if (_testService is null || IsBusy)
        {
            StatusText = _testService is null ? "Test service unavailable" : StatusText;
            return;
        }

        if (_main.SolutionService.CurrentSolution is null)
        {
            Projects.Clear();
            StatusText = "No solution loaded";
            return;
        }

        var testProjects = FindTestProjects();
        if (testProjects.Count == 0)
        {
            Projects.Clear();
            StatusText = "No test projects in solution";
            return;
        }

        IsBusy = true;
        StatusText = "Discovering tests…";
        try
        {
            Projects.Clear();
            var total = 0;
            foreach (var project in testProjects)
            {
                var tests = await _testService.DiscoverTestsAsync(project.FilePath, ct);
                if (tests.Count == 0)
                {
                    continue;
                }

                Projects.Add(BuildProjectNode(project, tests));
                total += tests.Count;
            }

            StatusText = total == 0
                ? "No tests found"
                : $"{total} test(s) in {Projects.Count} project(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Discovery failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunAllAsync(CancellationToken ct)
    {
        if (_main.SolutionService.CurrentSolution?.FilePath is not { } solutionPath)
        {
            StatusText = "No solution loaded";
            return;
        }

        await RunCoreAsync(solutionPath, filter: null, _ => true, ct);
    }

    [RelayCommand]
    private async Task RunFailedAsync(CancellationToken ct)
    {
        var failedFqns = EnumerateTestNodes()
            .Where(n => n.Outcome == TestOutcome.Failed && n.Test is not null)
            .Select(n => n.Test!.FullyQualifiedName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (failedFqns.Count == 0)
        {
            StatusText = "No failed tests to re-run";
            return;
        }

        if (_main.SolutionService.CurrentSolution?.FilePath is not { } solutionPath)
        {
            StatusText = "No solution loaded";
            return;
        }

        var filter = string.Join("|", failedFqns.Select(f => $"FullyQualifiedName={f}"));
        await RunCoreAsync(solutionPath, filter, n => n.Test is not null && failedFqns.Contains(n.Test.FullyQualifiedName), ct);
    }

    [RelayCommand]
    private async Task RunSelectedAsync(TestNodeViewModel? node, CancellationToken ct)
    {
        if (node is null || IsBusy)
        {
            return;
        }

        var targetPath = FindProjectNode(node)?.ProjectPath ?? _main.SolutionService.CurrentSolution?.FilePath;
        if (targetPath is null)
        {
            StatusText = "No solution loaded";
            return;
        }

        switch (node.Kind)
        {
            case TestNodeKind.Project:
                await RunCoreAsync(targetPath, null, _ => true, ct);
                break;
            case TestNodeKind.Test when node.Test is not null:
                await RunCoreAsync(targetPath, $"FullyQualifiedName={node.Test.FullyQualifiedName}",
                    n => ReferenceEquals(n, node), ct);
                break;
            case TestNodeKind.Class or TestNodeKind.Namespace when FirstTest(node) is { } first:
                var prefix = node.Kind == TestNodeKind.Class
                    ? ClassPrefix(first.Test!.FullyQualifiedName)
                    : NamespacePrefix(first.Test!.FullyQualifiedName);
                await RunCoreAsync(targetPath, $"FullyQualifiedName~{prefix}", n => IsDescendantOf(node, n), ct);
                break;
        }
    }

    [RelayCommand]
    private async Task NavigateToTestAsync(TestNodeViewModel? node, CancellationToken ct)
    {
        if (node?.Test?.CodeFilePath is not { } path)
        {
            return;
        }

        var editor = _main.Editor;
        if (editor is null)
        {
            return;
        }

        var docVm = FindOpenDocument(path);
        if (docVm is null)
        {
            await _main.EditorService.OpenDocumentAsync(path, ct);
            docVm = FindOpenDocument(path);
        }

        if (docVm is null)
        {
            return;
        }

        docVm.CursorLine = Math.Max(1, node.Test.CodeLine ?? 1);
        docVm.CursorColumn = 1;
        _main.ActivateEditorDocument();
    }

    private DocumentViewModel? FindOpenDocument(string path) =>
        _main.Editor?.OpenDocuments.FirstOrDefault(d =>
            string.Equals(d.Document.FilePath, path, StringComparison.OrdinalIgnoreCase));

    private async Task DiscoverSafeAsync()
    {
        try
        {
            await DiscoverAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Fire-and-forget on solution load; manual discover reports errors.
        }
    }

    private async Task RunCoreAsync(
        string targetPath, string? filter, Func<TestNodeViewModel, bool> inScope, CancellationToken ct)
    {
        if (_testService is null)
        {
            return;
        }

        // A run requested while discovery (or another run) is still in progress
        // waits for it instead of being silently dropped.
        var waitUntil = DateTime.UtcNow + TimeSpan.FromMinutes(5);
        while (IsBusy && DateTime.UtcNow < waitUntil)
        {
            StatusText = "Waiting for the current test operation…";
            await Task.Delay(500, ct);
        }
        if (IsBusy)
        {
            StatusText = "Another test operation is still running";
            return;
        }

        // dotnet test on a solution makes every project overwrite the same TRX file —
        // run each test project separately and merge the results.
        var targets = IsSolutionFile(targetPath)
            ? FindTestProjects().Select(p => p.FilePath).ToList()
            : [targetPath];

        IsBusy = true;
        StatusText = "Running tests…";
        foreach (var node in EnumerateTestNodes().Where(inScope))
        {
            node.Outcome = TestOutcome.Running;
        }

        try
        {
            var allResults = new List<TestInfo>();
            var totalDuration = TimeSpan.Zero;
            var errorOutput = false;
            foreach (var target in targets)
            {
                StatusText = $"Running tests… ({Path.GetFileNameWithoutExtension(target)})";
                var summary = await _testService.RunTestsAsync(target, filter, ct);
                allResults.AddRange(summary.Tests);
                totalDuration += summary.Duration;
                errorOutput |= summary.Tests.Count == 0 && summary.ErrorOutput is not null;
            }

            MergeResults(allResults);

            var passed = allResults.Count(t => t.Outcome == TestOutcome.Passed);
            var failed = allResults.Count(t => t.Outcome == TestOutcome.Failed);
            var skipped = allResults.Count(t => t.Outcome == TestOutcome.Skipped);
            StatusText = allResults.Count == 0 && errorOutput
                ? "Test run failed to produce results (build error?)"
                : $"{passed} passed, {failed} failed, {skipped} skipped ({totalDuration.TotalSeconds:F1}s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Run failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsSolutionFile(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    private void MergeResults(IReadOnlyList<TestInfo> results)
    {
        // Match run results to tree nodes two ways:
        // - by display name: xUnit theories are discovered one row per case whose
        //   FQN carries arguments, e.g. "Ns.Cls.Method(a: 1)" — the TRX testName
        //   matches that string exactly;
        // - by FQN (className.method, never carries arguments) — plain tests.
        var byDisplayName = results
            .GroupBy(r => r.DisplayName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, MergeRows, StringComparer.Ordinal);
        var byFqn = results
            .GroupBy(r => r.FullyQualifiedName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, MergeRows, StringComparer.Ordinal);

        foreach (var node in EnumerateTestNodes())
        {
            if (node.Outcome != TestOutcome.Running || node.Test is null)
            {
                continue; // not part of this run's scope
            }

            var fqn = node.Test.FullyQualifiedName;
            if (byDisplayName.TryGetValue(fqn, out var result) || byFqn.TryGetValue(fqn, out result))
            {
                node.Test = result with { ProjectPath = node.Test.ProjectPath };
                node.Outcome = result.Outcome;
                node.Duration = result.Duration;
            }
            else
            {
                node.Outcome = TestOutcome.NotRun;
                node.Duration = null;
            }
        }

        foreach (var project in Projects)
        {
            RecomputeAggregates(project);
        }

        OnPropertyChanged(nameof(DetailsText));
    }

    private static TestInfo MergeRows(IEnumerable<TestInfo> rows)
    {
        var list = rows.ToList();
        var first = list[0];
        if (list.Count == 1)
        {
            return first;
        }

        // Multiple cases under one key — worst outcome wins, durations sum,
        // failure details come from the first failing row.
        var failure = list.FirstOrDefault(r => r.Outcome == TestOutcome.Failed);
        return first with
        {
            Outcome = list.Aggregate(TestOutcome.Passed, (worst, r) => WorstOf(worst, r.Outcome)),
            Duration = list.Aggregate(TimeSpan.Zero, (sum, r) => sum + (r.Duration ?? TimeSpan.Zero)),
            ErrorMessage = failure?.ErrorMessage ?? first.ErrorMessage,
            StackTrace = failure?.StackTrace ?? first.StackTrace,
            CodeFilePath = list.FirstOrDefault(r => r.CodeFilePath is not null)?.CodeFilePath ?? first.CodeFilePath,
            CodeLine = list.FirstOrDefault(r => r.CodeLine is not null)?.CodeLine ?? first.CodeLine,
        };
    }

    private static TestOutcome WorstOf(TestOutcome a, TestOutcome b)
    {
        if (a == TestOutcome.Failed || b == TestOutcome.Failed) return TestOutcome.Failed;
        if (a == TestOutcome.NotRun || b == TestOutcome.NotRun) return TestOutcome.NotRun;
        if (a == TestOutcome.Skipped || b == TestOutcome.Skipped) return TestOutcome.Skipped;
        return TestOutcome.Passed;
    }

    private static TestOutcome RecomputeAggregates(TestNodeViewModel node)
    {
        if (node.Kind == TestNodeKind.Test)
        {
            return node.Outcome;
        }

        var childOutcomes = node.Children.Select(RecomputeAggregates).ToList();
        node.Outcome = Aggregate(childOutcomes);
        node.Duration = node.Children.Any(c => c.Duration is not null)
            ? node.Children.Aggregate(TimeSpan.Zero, (sum, c) => sum + (c.Duration ?? TimeSpan.Zero))
            : null;
        return node.Outcome;
    }

    private static TestOutcome Aggregate(IReadOnlyList<TestOutcome> outcomes)
    {
        if (outcomes.Count == 0) return TestOutcome.NotRun;
        if (outcomes.Any(o => o == TestOutcome.Failed)) return TestOutcome.Failed;
        if (outcomes.Any(o => o == TestOutcome.Running)) return TestOutcome.Running;
        if (outcomes.All(o => o == TestOutcome.NotRun)) return TestOutcome.NotRun;
        if (outcomes.All(o => o is TestOutcome.Passed or TestOutcome.Skipped))
        {
            return outcomes.Any(o => o == TestOutcome.Passed) ? TestOutcome.Passed : TestOutcome.Skipped;
        }
        return TestOutcome.NotRun;
    }

    private List<ProjectModel> FindTestProjects()
    {
        var projects = new List<ProjectModel>();
        foreach (var project in _main.SolutionService.GetLoadedProjects())
        {
            try
            {
                if (File.ReadAllText(project.FilePath).Contains("Microsoft.NET.Test.Sdk", StringComparison.Ordinal))
                {
                    projects.Add(project);
                }
            }
            catch (Exception)
            {
                // Unreadable project file — skip.
            }
        }
        return projects;
    }

    internal static TestNodeViewModel BuildProjectNode(ProjectModel project, IReadOnlyList<TestInfo> tests)
    {
        var projectNode = new TestNodeViewModel
        {
            Name = project.Name,
            Kind = TestNodeKind.Project,
            ProjectPath = project.FilePath,
        };

        foreach (var test in tests)
        {
            var (ns, cls, method) = SplitFqn(test.FullyQualifiedName);
            var parent = projectNode;
            if (ns.Length > 0)
            {
                parent = GetOrAddChild(parent, ns, TestNodeKind.Namespace);
            }
            if (cls.Length > 0)
            {
                parent = GetOrAddChild(parent, cls, TestNodeKind.Class);
            }
            parent.Children.Add(new TestNodeViewModel { Name = method, Kind = TestNodeKind.Test, Test = test });
        }

        SortRecursive(projectNode);
        return projectNode;
    }

    internal static (string Namespace, string Class, string Method) SplitFqn(string fqn)
    {
        // Parameterized cases carry arguments: "Ns.Cls.Method(a: 1, b: 2)" — strip them
        // for hierarchy purposes (arguments may contain dots) but keep them in the leaf name.
        var argsIndex = fqn.IndexOf('(');
        var path = argsIndex >= 0 ? fqn[..argsIndex] : fqn;
        var args = argsIndex >= 0 ? fqn[argsIndex..] : string.Empty;
        var segments = path.Split('.');
        return segments.Length switch
        {
            1 => (string.Empty, string.Empty, fqn),
            2 => (string.Empty, segments[0], segments[1] + args),
            _ => (string.Join('.', segments[..^2]), segments[^2], segments[^1] + args),
        };
    }

    private static TestNodeViewModel GetOrAddChild(TestNodeViewModel parent, string name, TestNodeKind kind)
    {
        var existing = parent.Children.FirstOrDefault(c => c.Kind == kind && c.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var node = new TestNodeViewModel { Name = name, Kind = kind };
        parent.Children.Add(node);
        return node;
    }

    private static void SortRecursive(TestNodeViewModel node)
    {
        var sorted = node.Children.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        node.Children.Clear();
        foreach (var child in sorted)
        {
            node.Children.Add(child);
            SortRecursive(child);
        }
    }

    private IEnumerable<TestNodeViewModel> EnumerateTestNodes() =>
        Enumerate(Projects).Where(n => n.Kind == TestNodeKind.Test);

    private static IEnumerable<TestNodeViewModel> Enumerate(IEnumerable<TestNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Enumerate(node.Children))
            {
                yield return child;
            }
        }
    }

    private TestNodeViewModel? FindProjectNode(TestNodeViewModel node) =>
        Projects.FirstOrDefault(p => p == node || Enumerate(p.Children).Any(c => ReferenceEquals(c, node)));

    private static TestNodeViewModel? FirstTest(TestNodeViewModel node) =>
        node.Kind == TestNodeKind.Test ? node : node.Children.Select(FirstTest).FirstOrDefault(t => t is not null);

    private static bool IsDescendantOf(TestNodeViewModel ancestor, TestNodeViewModel node) =>
        Enumerate(ancestor.Children).Any(c => ReferenceEquals(c, node));

    private static string ClassPrefix(string fqn) => fqn[..fqn.LastIndexOf('.')];

    private static string NamespacePrefix(string fqn)
    {
        var classPrefix = ClassPrefix(fqn);
        var lastDot = classPrefix.LastIndexOf('.');
        return lastDot < 0 ? classPrefix : classPrefix[..lastDot];
    }
}
