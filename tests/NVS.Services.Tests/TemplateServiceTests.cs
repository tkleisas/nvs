using NVS.Services.Template;

namespace NVS.Services.Tests;

public sealed class TemplateServiceTests : IDisposable
{
    private readonly TemplateService _service = new();
    private readonly string _tempDir;

    public TemplateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NVS_TemplateTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region GetProjectTemplates

    [Fact]
    public void GetProjectTemplates_ShouldReturnNonEmptyList()
    {
        var templates = _service.GetProjectTemplates();

        templates.Should().NotBeEmpty();
        templates.Should().HaveCountGreaterThanOrEqualTo(7);
    }

    [Theory]
    [InlineData("console", "Console App")]
    [InlineData("classlib", "Class Library")]
    [InlineData("webapi", "ASP.NET Core Web API")]
    [InlineData("xunit", "xUnit Test Project")]
    [InlineData("worker", "Worker Service")]
    public void GetProjectTemplates_ShouldContainExpectedTemplates(string shortName, string displayName)
    {
        var templates = _service.GetProjectTemplates();

        templates.Should().Contain(t => t.ShortName == shortName && t.DisplayName == displayName);
    }

    [Fact]
    public void GetProjectTemplates_AllShouldHaveFrameworks()
    {
        var templates = _service.GetProjectTemplates();

        foreach (var template in templates)
        {
            template.Frameworks.Should().NotBeEmpty($"template '{template.ShortName}' should have frameworks");
            template.Frameworks.Should().Contain("net10.0");
        }
    }

    #endregion

    #region GetFileTemplates

    [Fact]
    public void GetFileTemplates_ShouldReturnNonEmptyList()
    {
        var templates = _service.GetFileTemplates();

        templates.Should().NotBeEmpty();
        templates.Should().HaveCountGreaterThanOrEqualTo(8);
    }

    [Theory]
    [InlineData("class", "Class", ".cs")]
    [InlineData("interface", "Interface", ".cs")]
    [InlineData("record", "Record", ".cs")]
    [InlineData("enum", "Enum", ".cs")]
    [InlineData("struct", "Struct", ".cs")]
    [InlineData("exception", "Exception", ".cs")]
    [InlineData("static-class", "Static Class", ".cs")]
    [InlineData("abstract-class", "Abstract Class", ".cs")]
    public void GetFileTemplates_ShouldContainExpectedTemplates(string id, string displayName, string extension)
    {
        var templates = _service.GetFileTemplates();

        templates.Should().Contain(t => t.Id == id && t.DisplayName == displayName && t.Extension == extension);
    }

    [Fact]
    public void GetFileTemplates_AllShouldHaveContentWithPlaceholders()
    {
        var templates = _service.GetFileTemplates();

        foreach (var template in templates)
        {
            template.ContentTemplate.Should().Contain("{{Namespace}}", $"template '{template.Id}' should have namespace placeholder");
            template.ContentTemplate.Should().Contain("{{Name}}", $"template '{template.Id}' should have name placeholder");
            template.DefaultFileName.Should().NotBeNullOrWhiteSpace();
        }
    }

    #endregion

    #region CreateFileFromTemplate

    [Fact]
    public async Task CreateFileFromTemplate_WithClassTemplate_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "class", "MyClass", _tempDir, "MyApp.Models");

        filePath.Should().EndWith("MyClass.cs");
        File.Exists(filePath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("namespace MyApp.Models;");
        content.Should().Contain("public class MyClass");
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithInterfaceTemplate_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "interface", "IMyService", _tempDir, "MyApp.Core");

        filePath.Should().EndWith("IMyService.cs");
        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("namespace MyApp.Core;");
        content.Should().Contain("public interface IMyService");
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithRecordTemplate_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "record", "PersonDto", _tempDir, "MyApp.Dtos");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("namespace MyApp.Dtos;");
        content.Should().Contain("public sealed record PersonDto");
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithEnumTemplate_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "enum", "Status", _tempDir, "MyApp.Enums");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("namespace MyApp.Enums;");
        content.Should().Contain("public enum Status");
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithExceptionTemplate_ShouldHaveConstructors()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "exception", "AppException", _tempDir, "MyApp");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("public class AppException : Exception");
        content.Should().Contain("public AppException()");
        content.Should().Contain("public AppException(string message)");
        content.Should().Contain("public AppException(string message, Exception innerException)");
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithExistingFile_ShouldThrow()
    {
        await _service.CreateFileFromTemplateAsync("class", "Existing", _tempDir, "MyApp");

        var act = () => _service.CreateFileFromTemplateAsync("class", "Existing", _tempDir, "MyApp");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithUnknownTemplate_ShouldThrow()
    {
        var act = () => _service.CreateFileFromTemplateAsync("nonexistent", "Foo", _tempDir, "MyApp");

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unknown file template*");
    }

    [Theory]
    [InlineData("", "name", "dir", "ns")]
    [InlineData("class", "", "dir", "ns")]
    [InlineData("class", "name", "", "ns")]
    [InlineData("class", "name", "dir", "")]
    public async Task CreateFileFromTemplate_WithEmptyArgs_ShouldThrow(
        string templateId, string fileName, string directory, string namespaceName)
    {
        var dir = string.IsNullOrEmpty(directory) ? directory : _tempDir;
        var act = () => _service.CreateFileFromTemplateAsync(templateId, fileName, dir, namespaceName);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithNestedDirectory_ShouldCreateDirectory()
    {
        var nestedDir = Path.Combine(_tempDir, "Sub", "Nested");

        var filePath = await _service.CreateFileFromTemplateAsync(
            "class", "Deep", nestedDir, "MyApp.Sub.Nested");

        File.Exists(filePath).Should().BeTrue();
        Directory.Exists(nestedDir).Should().BeTrue();
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithStaticClass_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "static-class", "Helpers", _tempDir, "MyApp.Utils");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("public static class Helpers");
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithAbstractClass_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "abstract-class", "BaseEntity", _tempDir, "MyApp.Domain");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("public abstract class BaseEntity");
    }

    [Fact]
    public async Task CreateFileFromTemplate_WithStruct_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "struct", "Point", _tempDir, "MyApp.Geometry");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("public readonly struct Point");
    }

    #endregion

    #region InferNamespace

    [Fact]
    public void InferNamespace_WithProjectRootAndSubfolder_ShouldBuildNamespace()
    {
        var root = Path.Combine(_tempDir, "MyApp");
        var dir = Path.Combine(root, "Models", "Entities");

        var ns = TemplateService.InferNamespace(dir, root);

        ns.Should().Be("MyApp.Models.Entities");
    }

    [Fact]
    public void InferNamespace_WithProjectRootSameAsDir_ShouldReturnBaseName()
    {
        var root = Path.Combine(_tempDir, "MyApp");

        var ns = TemplateService.InferNamespace(root, root);

        ns.Should().Be("MyApp");
    }

    [Fact]
    public void InferNamespace_WithNullProjectRoot_ShouldUseDirName()
    {
        var dir = Path.Combine(_tempDir, "SomeDir");

        var ns = TemplateService.InferNamespace(dir, null);

        ns.Should().Be("SomeDir");
    }

    [Fact]
    public void InferNamespace_WithCustomRootNamespace_ShouldUseIt()
    {
        var root = Path.Combine(_tempDir, "MyApp");
        var dir = Path.Combine(root, "Services");

        var ns = TemplateService.InferNamespace(dir, root, "MyCompany.MyApp");

        ns.Should().Be("MyCompany.MyApp.Services");
    }

    [Fact]
    public void InferNamespace_WithOutsideDirectory_ShouldReturnBaseName()
    {
        var root = Path.Combine(_tempDir, "MyApp");
        var dir = Path.Combine(_tempDir, "Other");

        var ns = TemplateService.InferNamespace(dir, root);

        ns.Should().Be("MyApp");
    }

    #endregion

    #region CreateProjectAsync

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateProjectAsync_WithEmptyTemplateName_ShouldThrow(string templateName)
    {
        var act = () => _service.CreateProjectAsync(templateName, "MyApp", _tempDir);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateProjectAsync_WithEmptyProjectName_ShouldThrow(string projectName)
    {
        var act = () => _service.CreateProjectAsync("console", projectName, _tempDir);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateProjectAsync_WithEmptyOutputDirectory_ShouldThrow(string outputDir)
    {
        var act = () => _service.CreateProjectAsync("console", "MyApp", outputDir);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion
}
