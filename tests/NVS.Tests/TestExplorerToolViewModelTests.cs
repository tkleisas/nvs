using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class TestExplorerToolViewModelTests
{
    [Theory]
    [InlineData("Ns.Sub.Cls.Method", "Ns.Sub", "Cls", "Method")]
    [InlineData("Cls.Method", "", "Cls", "Method")]
    [InlineData("Method", "", "", "Method")]
    [InlineData("Ns.Cls.Method(input: \"a.b\", n: 1)", "Ns", "Cls", "Method(input: \"a.b\", n: 1)")]
    public void SplitFqn_SplitsIntoNamespaceClassMethod(string fqn, string ns, string cls, string method)
    {
        TestExplorerToolViewModel.SplitFqn(fqn).Should().Be((ns, cls, method));
    }

    [Fact]
    public void BuildProjectNode_GroupsTestsByNamespaceAndClass()
    {
        var project = Project("App.Tests", @"C:\src\App.Tests\App.Tests.csproj");
        var node = TestExplorerToolViewModel.BuildProjectNode(project,
        [
            Test("Ns.AlphaTests.First"),
            Test("Ns.BetaTests.Second"),
            Test("Ns.AlphaTests.Third"),
        ]);

        node.Kind.Should().Be(TestNodeKind.Project);
        var nsNode = node.Children.Should().ContainSingle().Subject;
        nsNode.Name.Should().Be("Ns");
        nsNode.Kind.Should().Be(TestNodeKind.Namespace);
        // Classes sorted alphabetically
        nsNode.Children.Select(c => c.Name).Should().Equal("AlphaTests", "BetaTests");
        nsNode.Children[0].Children.Select(c => c.Name).Should().Equal("First", "Third");
        nsNode.Children[0].Children[0].Kind.Should().Be(TestNodeKind.Test);
    }

    [Fact]
    public async Task Discover_PopulatesTreeFromTestProjectsOnly()
    {
        var (solution, dir) = CreateSolutionWithProjects();
        var service = Substitute.For<ITestExplorerService>();
        service.DiscoverTestsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Test("Ns.Cls.One"), Test("Ns.Cls.Two")]);

        var vm = new TestExplorerToolViewModel(CreateMain(solution), service);

        await vm.DiscoverCommand.ExecuteAsync(null);

        // Only the test project (references Microsoft.NET.Test.Sdk) is queried
        await service.Received(1).DiscoverTestsAsync(
            Arg.Is<string>(p => p.Contains("App.Tests.csproj")), Arg.Any<CancellationToken>());
        vm.Projects.Should().ContainSingle();
        vm.StatusText.Should().Be("2 test(s) in 1 project(s)");
    }

    [Fact]
    public async Task RunAll_MergesOutcomesAndAggregatesUpTheTree()
    {
        var (solution, _) = CreateSolutionWithProjects();
        var service = Substitute.For<ITestExplorerService>();
        service.DiscoverTestsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Test("Ns.Cls.Good"), Test("Ns.Cls.Bad")]);
        service.RunTestsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Summary(
                Test("Ns.Cls.Good", TestOutcome.Passed, TimeSpan.FromMilliseconds(10)),
                Test("Ns.Cls.Bad", TestOutcome.Failed, TimeSpan.FromMilliseconds(20))));

        var vm = new TestExplorerToolViewModel(CreateMain(solution), service);
        await vm.DiscoverCommand.ExecuteAsync(null);

        await vm.RunAllCommand.ExecuteAsync(null);

        var classNode = vm.Projects[0].Children[0].Children[0];
        classNode.Children.Single(c => c.Name == "Good").Outcome.Should().Be(TestOutcome.Passed);
        classNode.Children.Single(c => c.Name == "Bad").Outcome.Should().Be(TestOutcome.Failed);
        classNode.Outcome.Should().Be(TestOutcome.Failed);
        vm.Projects[0].Outcome.Should().Be(TestOutcome.Failed);
        vm.Projects[0].Duration.Should().Be(TimeSpan.FromMilliseconds(30));
        vm.StatusText.Should().Contain("1 passed, 1 failed, 0 skipped");
    }

    [Fact]
    public async Task RunFailed_RerunsOnlyFailedTestsWithExactFilter()
    {
        var (solution, _) = CreateSolutionWithProjects();
        var service = Substitute.For<ITestExplorerService>();
        service.DiscoverTestsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Test("Ns.Cls.Good"), Test("Ns.Cls.Bad")]);
        service.RunTestsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Summary(
                Test("Ns.Cls.Good", TestOutcome.Passed),
                Test("Ns.Cls.Bad", TestOutcome.Failed)));

        var vm = new TestExplorerToolViewModel(CreateMain(solution), service);
        await vm.DiscoverCommand.ExecuteAsync(null);
        await vm.RunAllCommand.ExecuteAsync(null);

        await vm.RunFailedCommand.ExecuteAsync(null);

        await service.Received(1).RunTestsAsync(
            Arg.Any<string>(),
            Arg.Is<string?>(f => f == "FullyQualifiedName=Ns.Cls.Bad"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunFailed_NoFailures_DoesNotInvokeService()
    {
        var (solution, _) = CreateSolutionWithProjects();
        var service = Substitute.For<ITestExplorerService>();
        service.DiscoverTestsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Test("Ns.Cls.Good")]);

        var vm = new TestExplorerToolViewModel(CreateMain(solution), service);
        await vm.DiscoverCommand.ExecuteAsync(null);

        await vm.RunFailedCommand.ExecuteAsync(null);

        await service.DidNotReceive().RunTestsAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        vm.StatusText.Should().Be("No failed tests to re-run");
    }

    [Fact]
    public async Task RunAll_TheoryCasesDiscoveredWithArguments_MatchByDisplayName()
    {
        // Regression: xUnit lists theory cases with arguments ("Ns.Cls.Method(a: 1)")
        // while TRX FQNs (className.method) never carry arguments — nodes must match
        // run results via the TRX display name.
        var caseFqn = "Ns.Cls.Cases(input: \"# Title\", expectedLevel: 1)";
        var (solution, _) = CreateSolutionWithProjects();
        var service = Substitute.For<ITestExplorerService>();
        service.DiscoverTestsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Test("Ns.Cls.Plain"), Test(caseFqn)]);
        service.RunTestsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Summary(
                Test("Ns.Cls.Plain", TestOutcome.Passed),
                Test("Ns.Cls.Cases", TestOutcome.Passed) with { DisplayName = caseFqn }));

        var vm = new TestExplorerToolViewModel(CreateMain(solution), service);
        await vm.DiscoverCommand.ExecuteAsync(null);
        await vm.RunAllCommand.ExecuteAsync(null);

        var classNode = vm.Projects[0].Children[0].Children[0];
        classNode.Children.Single(c => c.Name == "Plain").Outcome.Should().Be(TestOutcome.Passed);
        classNode.Children.Single(c => c.Name.StartsWith("Cases(", StringComparison.Ordinal))
            .Outcome.Should().Be(TestOutcome.Passed);
        vm.Projects[0].Outcome.Should().Be(TestOutcome.Passed);
    }

    [Fact]
    public async Task ParameterizedTests_SameFqn_MergeToWorstOutcome()
    {
        var (solution, _) = CreateSolutionWithProjects();
        var service = Substitute.For<ITestExplorerService>();
        service.DiscoverTestsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Test("Ns.Cls.Cases")]);
        service.RunTestsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Summary(
                Test("Ns.Cls.Cases", TestOutcome.Passed),
                Test("Ns.Cls.Cases", TestOutcome.Failed)));

        var vm = new TestExplorerToolViewModel(CreateMain(solution), service);
        await vm.DiscoverCommand.ExecuteAsync(null);
        await vm.RunAllCommand.ExecuteAsync(null);

        var testNode = vm.Projects[0].Children[0].Children[0].Children[0];
        testNode.Outcome.Should().Be(TestOutcome.Failed);
    }

    [Fact]
    public async Task DetailsText_FailedTest_ShowsMessageAndStackTrace()
    {
        var (solution, _) = CreateSolutionWithProjects();
        var service = Substitute.For<ITestExplorerService>();
        service.DiscoverTestsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Test("Ns.Cls.Bad")]);
        service.RunTestsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Summary(Test("Ns.Cls.Bad", TestOutcome.Failed) with
            {
                ErrorMessage = "boom",
                StackTrace = "at Ns.Cls.Bad()",
            }));

        var vm = new TestExplorerToolViewModel(CreateMain(solution), service);
        await vm.DiscoverCommand.ExecuteAsync(null);
        await vm.RunAllCommand.ExecuteAsync(null);

        vm.SelectedNode = vm.Projects[0].Children[0].Children[0].Children[0];

        vm.DetailsText.Should().Contain("boom").And.Contain("at Ns.Cls.Bad()");
    }

    [Fact]
    public async Task RunAll_SolutionTarget_RunsEachTestProjectSeparately()
    {
        // Regression: dotnet test on a solution overwrites one shared TRX per project,
        // so the explorer must run projects one by one and merge.
        var (solution, _) = CreateSolutionWithProjects(twoTestProjects: true);
        var service = Substitute.For<ITestExplorerService>();
        service.DiscoverTestsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Test("Ns.Cls.One")]);
        service.RunTestsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Summary(Test("Ns.Cls.One", TestOutcome.Passed)));

        var vm = new TestExplorerToolViewModel(CreateMain(solution), service);
        await vm.DiscoverCommand.ExecuteAsync(null);
        await vm.RunAllCommand.ExecuteAsync(null);

        await service.Received(1).RunTestsAsync(
            Arg.Is<string>(p => p.EndsWith("App.Tests.csproj")), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await service.Received(1).RunTestsAsync(
            Arg.Is<string>(p => p.EndsWith("App2.Tests.csproj")), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await service.DidNotReceive().RunTestsAsync(
            Arg.Is<string>(p => p.EndsWith(".slnx")), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discover_RunsProjectDiscoveryInParallel()
    {
        var (solution, _) = CreateSolutionWithProjects(twoTestProjects: true);
        var service = Substitute.For<ITestExplorerService>();

        var inFlight = 0;
        var maxInFlight = 0;
        var sync = new object();
        service.DiscoverTestsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var now = Interlocked.Increment(ref inFlight);
                lock (sync)
                {
                    maxInFlight = Math.Max(maxInFlight, now);
                }
                await Task.Delay(100);
                Interlocked.Decrement(ref inFlight);
                return (IReadOnlyList<TestInfo>)[Test("Ns.Cls.One")];
            });

        var vm = new TestExplorerToolViewModel(CreateMain(solution), service);
        await vm.DiscoverCommand.ExecuteAsync(null);

        maxInFlight.Should().BeGreaterThan(1, "independent project discoveries should overlap");
        vm.Projects.Should().HaveCount(2);
    }

    private static TestRunSummary Summary(params TestInfo[] tests) => new()
    {
        ExitCode = tests.Any(t => t.Outcome == TestOutcome.Failed) ? 1 : 0,
        Tests = tests,
        Duration = TimeSpan.FromSeconds(1),
    };

    private static TestInfo Test(string fqn, TestOutcome outcome = TestOutcome.NotRun, TimeSpan? duration = null) => new()
    {
        FullyQualifiedName = fqn,
        DisplayName = fqn.Split('.')[^1],
        Outcome = outcome,
        Duration = duration,
    };

    private static ProjectModel Project(string name, string path) => new()
    {
        FilePath = path,
        Name = name,
        Sdk = "Microsoft.NET.Sdk",
        TargetFramework = "net10.0",
    };

    private static (ISolutionService Service, string Directory) CreateSolutionWithProjects(bool twoTestProjects = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nvs-test-explorer-vm-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);

        var projects = new List<ProjectModel>();
        var testProjectPath = Path.Combine(dir, "App.Tests.csproj");
        File.WriteAllText(testProjectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>" +
            "<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.11.1\" />" +
            "</ItemGroup></Project>");
        projects.Add(Project("App.Tests", testProjectPath));

        if (twoTestProjects)
        {
            var testProject2Path = Path.Combine(dir, "App2.Tests.csproj");
            File.WriteAllText(testProject2Path,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>" +
                "<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.11.1\" />" +
                "</ItemGroup></Project>");
            projects.Add(Project("App2.Tests", testProject2Path));
        }

        var libraryProjectPath = Path.Combine(dir, "App.csproj");
        File.WriteAllText(libraryProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        projects.Add(Project("App", libraryProjectPath));

        var solutionService = Substitute.For<ISolutionService>();
        solutionService.CurrentSolution.Returns(new SolutionModel
        {
            FilePath = Path.Combine(dir, "App.slnx"),
            Name = "App",
            Format = SolutionFormat.Slnx,
        });
        solutionService.GetLoadedProjects().Returns(projects.ToArray());
        return (solutionService, dir);
    }

    private static MainViewModel CreateMain(ISolutionService solutionService)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());
        return new MainViewModel(
            Substitute.For<IWorkspaceService>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IFileSystemService>(),
            new EditorViewModel(Substitute.For<IEditorService>(), Substitute.For<IFileSystemService>()),
            Substitute.For<IGitService>(),
            Substitute.For<ITerminalService>(),
            settings,
            solutionService,
            Substitute.For<IBuildService>());
    }
}
