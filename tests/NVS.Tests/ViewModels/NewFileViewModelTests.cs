using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.ViewModels;

namespace NVS.Tests.ViewModels;

public sealed class NewFileViewModelTests : IDisposable
{
    private readonly string _tempDir;

    public NewFileViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NVS_NewFileTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static ITemplateService CreateMockTemplateService()
    {
        var service = Substitute.For<ITemplateService>();
        service.GetFileTemplates().Returns(new List<FileTemplate>
        {
            new()
            {
                Id = "class",
                DisplayName = "Class",
                DefaultFileName = "NewClass",
                Extension = ".cs",
                ContentTemplate = "namespace {{Namespace}};\n\npublic class {{Name}}\n{\n}\n",
                Icon = "🟢",
            },
            new()
            {
                Id = "interface",
                DisplayName = "Interface",
                DefaultFileName = "INewInterface",
                Extension = ".cs",
                ContentTemplate = "namespace {{Namespace}};\n\npublic interface {{Name}}\n{\n}\n",
                Icon = "🔵",
            },
        });
        return service;
    }

    [Fact]
    public void Constructor_ShouldPopulateTemplates()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);

        vm.Templates.Should().HaveCount(2);
        vm.SelectedTemplate.Should().NotBeNull();
        vm.SelectedTemplate!.Id.Should().Be("class");
    }

    [Fact]
    public void Constructor_ShouldSetDefaultFileName()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);

        vm.FileName.Should().Be("NewClass");
    }

    [Fact]
    public void Constructor_ShouldInferNamespace()
    {
        var projectRoot = Path.Combine(_tempDir, "MyApp");
        var subDir = Path.Combine(projectRoot, "Models");

        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, subDir, projectRoot);

        vm.NamespaceName.Should().Be("MyApp.Models");
    }

    [Fact]
    public void SelectedTemplate_WhenChanged_ShouldUpdateFileName()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);

        vm.SelectedTemplate = vm.Templates[1]; // interface

        vm.FileName.Should().Be("INewInterface");
    }

    [Fact]
    public void SelectedTemplate_WhenChanged_ShouldUpdatePreview()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);

        vm.Preview.Should().Contain("public class");

        vm.SelectedTemplate = vm.Templates[1];

        vm.Preview.Should().Contain("public interface");
    }

    [Fact]
    public void FileName_WhenSet_ShouldUpdatePreview()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);

        vm.FileName = "MyEntity";

        vm.Preview.Should().Contain("public class MyEntity");
    }

    [Fact]
    public void FileName_WhenSet_ShouldClearErrorMessage()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);
        vm.ErrorMessage = "Some error";

        vm.FileName = "NewName";

        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void NamespaceName_WhenSet_ShouldUpdatePreview()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);

        vm.NamespaceName = "MyApp.Domain";

        vm.Preview.Should().Contain("namespace MyApp.Domain;");
    }

    [Fact]
    public void CreateFileCommand_WithNoTemplate_ShouldNotExecute()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);
        vm.SelectedTemplate = null;

        vm.CreateFileCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CreateFileCommand_WithEmptyFileName_ShouldNotExecute()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);
        vm.FileName = "";

        vm.CreateFileCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CreateFileCommand_WithEmptyNamespace_ShouldNotExecute()
    {
        var service = CreateMockTemplateService();
        var vm = new NewFileViewModel(service, _tempDir);
        vm.NamespaceName = "";

        vm.CreateFileCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task CreateFileCommand_WhenSuccessful_ShouldSetCreatedPath()
    {
        var expectedPath = Path.Combine(_tempDir, "MyClass.cs");
        var service = CreateMockTemplateService();
        service.CreateFileFromTemplateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(expectedPath);

        var vm = new NewFileViewModel(service, _tempDir);
        vm.FileName = "MyClass";
        vm.NamespaceName = "MyApp";

        var closeFired = false;
        vm.RequestClose += (_, _) => closeFired = true;

        await vm.CreateFileCommand.ExecuteAsync(null);

        vm.CreatedFilePath.Should().Be(expectedPath);
        closeFired.Should().BeTrue();
    }

    [Fact]
    public async Task CreateFileCommand_WhenFileExists_ShouldSetErrorMessage()
    {
        var service = CreateMockTemplateService();
        service.CreateFileFromTemplateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("File 'Existing.cs' already exists."));

        var vm = new NewFileViewModel(service, _tempDir);
        vm.FileName = "Existing";
        vm.NamespaceName = "MyApp";

        await vm.CreateFileCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateFileCommand_WhenServiceThrows_ShouldSetErrorMessage()
    {
        var service = CreateMockTemplateService();
        service.CreateFileFromTemplateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("Disk full"));

        var vm = new NewFileViewModel(service, _tempDir);
        vm.FileName = "MyClass";
        vm.NamespaceName = "MyApp";

        await vm.CreateFileCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("Disk full");
    }
}
