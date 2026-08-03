using NVS.Core.Models;
using NVS.Services.Testing;

namespace NVS.Services.Tests;

public class TestExplorerServiceParsingTests
{
    private const string ListTestsOutput = """
        Determining projects to restore...
          All projects are up-to-date for restore.
        Microsoft (R) Test Execution Command Line Tool Version 17.11.1
        Copyright (c) Microsoft Corporation.  All rights reserved.

        The following Tests are available:
            NVS.Tests.FooTests.First_Works
            NVS.Tests.FooTests.Second_Works
            NVS.Services.Tests.BarTests.Other_Case
        """;

    [Fact]
    public void ParseListedTests_ExtractsFqnsAfterMarker()
    {
        var tests = TestExplorerService.ParseListedTests(ListTestsOutput, "proj.csproj").ToList();

        tests.Should().HaveCount(3);
        tests[0].FullyQualifiedName.Should().Be("NVS.Tests.FooTests.First_Works");
        tests[0].DisplayName.Should().Be("First_Works");
        tests[0].ProjectPath.Should().Be("proj.csproj");
        tests[2].FullyQualifiedName.Should().Be("NVS.Services.Tests.BarTests.Other_Case");
    }

    [Fact]
    public void ParseListedTests_NoMarker_ReturnsEmpty()
    {
        TestExplorerService.ParseListedTests("some random output\nwith lines").Should().BeEmpty();
    }

    [Fact]
    public void IsSelfHostedRun_TargetUnderProcessTree_ReturnsTrue()
    {
        // The test host runs from the repo's test output, so the repo solution is "self-hosted".
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var solutionPath = Path.Combine(repoRoot, "NVS.slnx");
        if (!File.Exists(solutionPath))
        {
            return; // layout assumption broken — nothing to assert against
        }

        TestExplorerService.IsSelfHostedRun(solutionPath).Should().BeTrue();
    }

    [Fact]
    public void IsSelfHostedRun_UnrelatedTarget_ReturnsFalse()
    {
        TestExplorerService.IsSelfHostedRun(
            Path.Combine(Path.GetTempPath(), "some-other-solution", "App.slnx")).Should().BeFalse();
    }

    private const string Trx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun id="run-1" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult executionId="e1" testId="t1" testName="NVS.Tests.FooTests.First_Works" outcome="Passed" duration="00:00:00.0123456" />
            <UnitTestResult executionId="e2" testId="t2" testName="NVS.Tests.FooTests.Second_Works" outcome="Failed" duration="00:00:00.2345678">
              <Output>
                <ErrorInfo>
                  <Message>Expected 1 to be 2</Message>
                  <StackTrace>   at NVS.Tests.FooTests.Second_Works() in C:\src\FooTests.cs:line 42</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
            <UnitTestResult executionId="e3" testId="t3" testName="NVS.Tests.FooTests.Skipped_Test" outcome="NotExecuted" duration="00:00:00.0000001" />
          </Results>
          <TestDefinitions>
            <UnitTest name="NVS.Tests.FooTests.First_Works" id="t1">
              <TestMethod codeFile="C:\src\FooTests.cs" lineNumber="10" className="NVS.Tests.FooTests" name="First_Works" />
            </UnitTest>
            <UnitTest name="NVS.Tests.FooTests.Second_Works" id="t2">
              <TestMethod codeFile="C:\src\FooTests.cs" lineNumber="42" className="NVS.Tests.FooTests" name="Second_Works" />
            </UnitTest>
            <UnitTest name="NVS.Tests.FooTests.Skipped_Test" id="t3">
              <TestMethod className="NVS.Tests.FooTests" name="Skipped_Test" />
            </UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    [Fact]
    public void ParseTrxResults_MapsOutcomesDurationsAndSourceLocations()
    {
        var tests = TestExplorerService.ParseTrxResults(Trx);

        tests.Should().HaveCount(3);

        var passed = tests[0];
        passed.FullyQualifiedName.Should().Be("NVS.Tests.FooTests.First_Works");
        passed.Outcome.Should().Be(TestOutcome.Passed);
        passed.Duration.Should().BeCloseTo(TimeSpan.FromMilliseconds(12.3), TimeSpan.FromMilliseconds(1));
        passed.CodeFilePath.Should().Be(@"C:\src\FooTests.cs");
        passed.CodeLine.Should().Be(10);
        passed.ErrorMessage.Should().BeNull();

        var failed = tests[1];
        failed.Outcome.Should().Be(TestOutcome.Failed);
        failed.ErrorMessage.Should().Be("Expected 1 to be 2");
        failed.StackTrace.Should().Contain("FooTests.cs:line 42");
        failed.CodeLine.Should().Be(42);

        var skipped = tests[2];
        skipped.Outcome.Should().Be(TestOutcome.Skipped);
        skipped.CodeFilePath.Should().BeNull();
        skipped.CodeLine.Should().BeNull();
    }
}
