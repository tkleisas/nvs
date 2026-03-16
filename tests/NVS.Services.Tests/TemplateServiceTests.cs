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
        templates.Should().HaveCountGreaterThanOrEqualTo(11);
    }

    [Theory]
    [InlineData("console", "Console App")]
    [InlineData("classlib", "Class Library")]
    [InlineData("webapi", "ASP.NET Core Web API")]
    [InlineData("xunit", "xUnit Test Project")]
    [InlineData("worker", "Worker Service")]
    [InlineData("java-console", "Java Console App")]
    [InlineData("java-library", "Java Library")]
    [InlineData("php-console", "PHP Console App")]
    [InlineData("php-library", "PHP Library")]
    public void GetProjectTemplates_ShouldContainExpectedTemplates(string shortName, string displayName)
    {
        var templates = _service.GetProjectTemplates();

        templates.Should().Contain(t => t.ShortName == shortName && t.DisplayName == displayName);
    }

    [Fact]
    public void GetProjectTemplates_DotNetTemplates_ShouldHaveFrameworks()
    {
        var templates = _service.GetProjectTemplates()
            .Where(t => t.ProjectSystem == NVS.Core.Enums.ProjectSystem.DotNet);

        foreach (var template in templates)
        {
            template.Frameworks.Should().NotBeEmpty($"template '{template.ShortName}' should have frameworks");
            template.Frameworks.Should().Contain("net10.0");
        }
    }

    [Fact]
    public void GetProjectTemplates_JavaTemplates_ShouldBeMaven()
    {
        var templates = _service.GetProjectTemplates()
            .Where(t => t.DefaultLanguage == "Java");

        templates.Should().HaveCount(2);
        foreach (var template in templates)
        {
            template.ProjectSystem.Should().Be(NVS.Core.Enums.ProjectSystem.Maven);
            template.Frameworks.Should().BeEmpty();
        }
    }

    [Fact]
    public void GetProjectTemplates_PhpTemplates_ShouldBeComposer()
    {
        var templates = _service.GetProjectTemplates()
            .Where(t => t.DefaultLanguage == "PHP");

        templates.Should().HaveCount(2);
        foreach (var template in templates)
        {
            template.ProjectSystem.Should().Be(NVS.Core.Enums.ProjectSystem.Composer);
            template.Frameworks.Should().BeEmpty();
        }
    }

    #endregion

    #region GetFileTemplates

    [Fact]
    public void GetFileTemplates_ShouldReturnNonEmptyList()
    {
        var templates = _service.GetFileTemplates();

        templates.Should().NotBeEmpty();
        templates.Should().HaveCountGreaterThanOrEqualTo(16);
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
    [InlineData("java-class", "Java Class", ".java")]
    [InlineData("java-interface", "Java Interface", ".java")]
    [InlineData("java-enum", "Java Enum", ".java")]
    [InlineData("java-record", "Java Record", ".java")]
    [InlineData("php-class", "PHP Class", ".php")]
    [InlineData("php-interface", "PHP Interface", ".php")]
    [InlineData("php-enum", "PHP Enum", ".php")]
    [InlineData("php-trait", "PHP Trait", ".php")]
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

    [Fact]
    public async Task CreateFileFromTemplate_JavaClass_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "java-class", "MyService", _tempDir, "com.example");

        File.Exists(filePath).Should().BeTrue();
        filePath.Should().EndWith(".java");
        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("package com.example;");
        content.Should().Contain("public class MyService");
    }

    [Fact]
    public async Task CreateFileFromTemplate_JavaInterface_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "java-interface", "Drawable", _tempDir, "com.example");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("package com.example;");
        content.Should().Contain("public interface Drawable");
    }

    [Fact]
    public async Task CreateFileFromTemplate_JavaEnum_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "java-enum", "Color", _tempDir, "com.example");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("public enum Color");
    }

    [Fact]
    public async Task CreateFileFromTemplate_JavaRecord_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "java-record", "Point", _tempDir, "com.example");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("public record Point()");
    }

    [Fact]
    public async Task CreateFileFromTemplate_PhpClass_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "php-class", "UserService", _tempDir, "App\\Services");

        File.Exists(filePath).Should().BeTrue();
        filePath.Should().EndWith(".php");
        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("<?php");
        content.Should().Contain("namespace App\\Services;");
        content.Should().Contain("class UserService");
    }

    [Fact]
    public async Task CreateFileFromTemplate_PhpInterface_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "php-interface", "Loggable", _tempDir, "App\\Contracts");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("interface Loggable");
    }

    [Fact]
    public async Task CreateFileFromTemplate_PhpEnum_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "php-enum", "Status", _tempDir, "App\\Enums");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("enum Status");
    }

    [Fact]
    public async Task CreateFileFromTemplate_PhpTrait_ShouldCreateCorrectFile()
    {
        var filePath = await _service.CreateFileFromTemplateAsync(
            "php-trait", "HasTimestamps", _tempDir, "App\\Traits");

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("trait HasTimestamps");
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

    [Fact]
    public async Task CreateProjectAsync_WithUnknownTemplate_ShouldThrow()
    {
        var act = () => _service.CreateProjectAsync("nonexistent", "MyApp", _tempDir);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*nonexistent*");
    }

    [Fact]
    public async Task CreateMavenProjectAsync_Console_ShouldCreateProjectStructure()
    {
        var projectDir = await _service.CreateProjectAsync(
            "java-console", "MyJavaApp", _tempDir);

        projectDir.Should().EndWith("MyJavaApp");
        Directory.Exists(projectDir).Should().BeTrue();

        // Verify pom.xml
        var pomPath = Path.Combine(projectDir, "pom.xml");
        File.Exists(pomPath).Should().BeTrue();
        var pomContent = await File.ReadAllTextAsync(pomPath);
        pomContent.Should().Contain("<artifactId>myjavaapp</artifactId>");
        pomContent.Should().Contain("<maven.compiler.source>21</maven.compiler.source>");
        pomContent.Should().Contain("<exec.mainClass>com.example.App</exec.mainClass>");

        // Verify main source
        var appPath = Path.Combine(projectDir, "src", "main", "java", "com", "example", "App.java");
        File.Exists(appPath).Should().BeTrue();
        var appContent = await File.ReadAllTextAsync(appPath);
        appContent.Should().Contain("public class App");
        appContent.Should().Contain("public static void main");

        // Verify test source
        var testPath = Path.Combine(projectDir, "src", "test", "java", "com", "example", "AppTest.java");
        File.Exists(testPath).Should().BeTrue();
    }

    [Fact]
    public async Task CreateMavenProjectAsync_Library_ShouldCreateProjectStructure()
    {
        var projectDir = await _service.CreateProjectAsync(
            "java-library", "MyJavaLib", _tempDir);

        Directory.Exists(projectDir).Should().BeTrue();

        // Verify pom.xml has no mainClass
        var pomContent = await File.ReadAllTextAsync(Path.Combine(projectDir, "pom.xml"));
        pomContent.Should().NotContain("exec.mainClass");

        // Verify no App.java for library
        var appPath = Path.Combine(projectDir, "src", "main", "java", "com", "example", "App.java");
        File.Exists(appPath).Should().BeFalse();

        // Verify test source
        var testPath = Path.Combine(projectDir, "src", "test", "java", "com", "example", "LibraryTest.java");
        File.Exists(testPath).Should().BeTrue();
    }

    [Fact]
    public async Task CreateComposerProjectAsync_Console_ShouldCreateProjectStructure()
    {
        var projectDir = await _service.CreateProjectAsync(
            "php-console", "MyPhpApp", _tempDir);

        projectDir.Should().EndWith("MyPhpApp");
        Directory.Exists(projectDir).Should().BeTrue();

        // Verify composer.json
        var composerPath = Path.Combine(projectDir, "composer.json");
        File.Exists(composerPath).Should().BeTrue();
        var composerContent = await File.ReadAllTextAsync(composerPath);
        composerContent.Should().Contain("\"myvendor/myphpapp\"");
        composerContent.Should().Contain("\"type\": \"project\"");
        composerContent.Should().Contain("\"php\": \">=8.2\"");
        composerContent.Should().Contain("\"MyPhpApp\\\\\"");

        // Verify entry point
        var binPath = Path.Combine(projectDir, "bin", "app.php");
        File.Exists(binPath).Should().BeTrue();
        var binContent = await File.ReadAllTextAsync(binPath);
        binContent.Should().Contain("use MyPhpApp\\App;");

        // Verify App.php
        var appPath = Path.Combine(projectDir, "src", "App.php");
        File.Exists(appPath).Should().BeTrue();
        var appContent = await File.ReadAllTextAsync(appPath);
        appContent.Should().Contain("namespace MyPhpApp;");
        appContent.Should().Contain("class App");
    }

    [Fact]
    public async Task CreateComposerProjectAsync_Library_ShouldCreateProjectStructure()
    {
        var projectDir = await _service.CreateProjectAsync(
            "php-library", "MyPhpLib", _tempDir);

        Directory.Exists(projectDir).Should().BeTrue();

        // Verify composer.json
        var composerContent = await File.ReadAllTextAsync(Path.Combine(projectDir, "composer.json"));
        composerContent.Should().Contain("\"type\": \"library\"");

        // Verify directories exist
        Directory.Exists(Path.Combine(projectDir, "src")).Should().BeTrue();
        Directory.Exists(Path.Combine(projectDir, "tests")).Should().BeTrue();

        // No bin/app.php for library
        File.Exists(Path.Combine(projectDir, "bin", "app.php")).Should().BeFalse();
    }

    #endregion
}
