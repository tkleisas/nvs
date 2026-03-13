using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.ViewModels;

namespace NVS.Tests.ViewModels;

public sealed class NewProjectViewModelTests
{
    private static ITemplateService CreateMockTemplateService()
    {
        var service = Substitute.For<ITemplateService>();
        service.GetProjectTemplates().Returns(new List<ProjectTemplate>
        {
            new()
            {
                ShortName = "console",
                DisplayName = "Console App",
                Description = "A project for creating a console application",
                DefaultLanguage = "C#",
                Tags = ["Console"],
                Frameworks = ["net10.0", "net9.0", "net8.0"],
            },
            new()
            {
                ShortName = "classlib",
                DisplayName = "Class Library",
                Description = "A project for creating a class library",
                DefaultLanguage = "C#",
                Tags = ["Library"],
                Frameworks = ["net10.0", "net9.0"],
            },
        });
        return service;
    }

    [Fact]
    public void Constructor_ShouldPopulateTemplates()
    {
        var service = CreateMockTemplateService();
        var vm = new NewProjectViewModel(service);

        vm.Templates.Should().HaveCount(2);
        vm.SelectedTemplate.Should().NotBeNull();
        vm.SelectedTemplate!.ShortName.Should().Be("console");
    }

    [Fact]
    public void Constructor_ShouldSetDefaultProjectName()
    {
        var service = CreateMockTemplateService();
        var vm = new NewProjectViewModel(service);

        vm.ProjectName.Should().Be("MyApp");
    }

    [Fact]
    public void Constructor_ShouldSetDefaultLocation()
    {
        var service = CreateMockTemplateService();
        var vm = new NewProjectViewModel(service);

        vm.Location.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SelectedTemplate_WhenChanged_ShouldUpdateFrameworks()
    {
        var service = CreateMockTemplateService();
        var vm = new NewProjectViewModel(service);

        vm.SelectedTemplate = vm.Templates[1]; // classlib

        vm.AvailableFrameworks.Should().BeEquivalentTo(["net10.0", "net9.0"]);
    }

    [Fact]
    public void ProjectName_WhenSet_ShouldClearErrorMessage()
    {
        var service = CreateMockTemplateService();
        var vm = new NewProjectViewModel(service);
        vm.ErrorMessage = "Some error";

        vm.ProjectName = "NewName";

        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Location_WhenSet_ShouldClearErrorMessage()
    {
        var service = CreateMockTemplateService();
        var vm = new NewProjectViewModel(service);
        vm.ErrorMessage = "Some error";

        vm.Location = Path.Combine(Path.GetTempPath(), "new", "path");

        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void CreateProjectCommand_WithNoTemplate_ShouldNotExecute()
    {
        var service = CreateMockTemplateService();
        var vm = new NewProjectViewModel(service);
        vm.SelectedTemplate = null;

        vm.CreateProjectCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CreateProjectCommand_WithEmptyName_ShouldNotExecute()
    {
        var service = CreateMockTemplateService();
        var vm = new NewProjectViewModel(service);
        vm.ProjectName = "";

        vm.CreateProjectCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CreateProjectCommand_WithEmptyLocation_ShouldNotExecute()
    {
        var service = CreateMockTemplateService();
        var vm = new NewProjectViewModel(service);
        vm.Location = "";

        vm.CreateProjectCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task CreateProjectCommand_WhenSuccessful_ShouldSetCreatedPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NVS_Test_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var service = CreateMockTemplateService();
            service.CreateProjectAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(Path.Combine(tempDir, "MyApp"));

            var vm = new NewProjectViewModel(service);
            vm.Location = tempDir;
            vm.ProjectName = "MyApp";

            var closeFired = false;
            vm.RequestClose += (_, _) => closeFired = true;

            await vm.CreateProjectCommand.ExecuteAsync(null);

            vm.CreatedProjectPath.Should().Be(Path.Combine(tempDir, "MyApp"));
            closeFired.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CreateProjectCommand_WhenServiceThrows_ShouldSetErrorMessage()
    {
        var service = CreateMockTemplateService();
        service.CreateProjectAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("dotnet new failed"));

        var vm = new NewProjectViewModel(service);
        vm.Location = Path.GetTempPath();
        vm.ProjectName = "FailApp_" + Guid.NewGuid().ToString("N")[..8];

        await vm.CreateProjectCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("dotnet new failed");
    }
}
