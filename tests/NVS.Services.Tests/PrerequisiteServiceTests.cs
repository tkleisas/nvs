using NVS.Core.Enums;
using NVS.Services.Prerequisites;

namespace NVS.Services.Tests;

public sealed class PrerequisiteServiceTests
{
    private readonly PrerequisiteService _service = new();

    #region Prerequisite Definitions

    [Fact]
    public void GetPrerequisiteDefinitions_ShouldContainAllExpectedLanguages()
    {
        var definitions = PrerequisiteService.GetPrerequisiteDefinitions();

        definitions.Should().ContainKey(Language.CSharp);
        definitions.Should().ContainKey(Language.Java);
        definitions.Should().ContainKey(Language.Php);
        definitions.Should().ContainKey(Language.Python);
        definitions.Should().ContainKey(Language.JavaScript);
        definitions.Should().ContainKey(Language.TypeScript);
        definitions.Should().ContainKey(Language.Rust);
        definitions.Should().ContainKey(Language.Go);
        definitions.Should().ContainKey(Language.Cpp);
        definitions.Should().ContainKey(Language.C);
    }

    [Theory]
    [InlineData(Language.CSharp, "dotnet", ".NET SDK")]
    [InlineData(Language.Java, "java", "Java JDK")]
    [InlineData(Language.Php, "php", "PHP")]
    [InlineData(Language.Python, "python", "Python")]
    [InlineData(Language.JavaScript, "node", "Node.js")]
    [InlineData(Language.TypeScript, "node", "Node.js")]
    [InlineData(Language.Rust, "rustc", "Rust")]
    [InlineData(Language.Go, "go", "Go")]
    [InlineData(Language.Cpp, "gcc", "C/C++ Compiler")]
    [InlineData(Language.C, "gcc", "C/C++ Compiler")]
    public void GetPrerequisiteDefinitions_ShouldHaveCorrectBinaryAndDisplayName(
        Language language, string expectedBinary, string expectedDisplayName)
    {
        var definitions = PrerequisiteService.GetPrerequisiteDefinitions();

        definitions[language].BinaryName.Should().Be(expectedBinary);
        definitions[language].DisplayName.Should().Be(expectedDisplayName);
    }

    [Fact]
    public void GetPrerequisiteDefinitions_AllShouldHaveInstallHints()
    {
        var definitions = PrerequisiteService.GetPrerequisiteDefinitions();

        foreach (var def in definitions.Values)
        {
            def.InstallHint.Should().NotBeNullOrWhiteSpace();
            def.InstallHint.Should().Contain("http", because: "install hints should include a URL");
        }
    }

    [Fact]
    public void GetPrerequisiteDefinitions_ShouldNotContainMarkdownOrXml()
    {
        var definitions = PrerequisiteService.GetPrerequisiteDefinitions();

        definitions.Should().NotContainKey(Language.Markdown);
        definitions.Should().NotContainKey(Language.Xml);
        definitions.Should().NotContainKey(Language.Json);
        definitions.Should().NotContainKey(Language.Yaml);
        definitions.Should().NotContainKey(Language.Toml);
        definitions.Should().NotContainKey(Language.Html);
        definitions.Should().NotContainKey(Language.Css);
        definitions.Should().NotContainKey(Language.Sql);
        definitions.Should().NotContainKey(Language.Unknown);
    }

    #endregion

    #region CheckPrerequisitesAsync

    [Fact]
    public async Task CheckPrerequisitesAsync_WithNoLanguages_ShouldReturnEmpty()
    {
        var result = await _service.CheckPrerequisitesAsync([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_WithUnknownLanguage_ShouldReturnEmpty()
    {
        var result = await _service.CheckPrerequisitesAsync([Language.Unknown]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_WithMarkupLanguages_ShouldReturnEmpty()
    {
        var result = await _service.CheckPrerequisitesAsync(
            [Language.Markdown, Language.Xml, Language.Json, Language.Html, Language.Css]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_WithCancellation_ShouldThrow()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _service.CheckPrerequisitesAsync(
            [Language.CSharp, Language.Java], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_JsAndTs_ShouldDeduplicateNode()
    {
        // Both JS and TS need "node" — should only appear once if missing
        var result = await _service.CheckPrerequisitesAsync(
            [Language.JavaScript, Language.TypeScript]);

        var nodeResults = result.Where(r => r.BinaryName == "node").ToList();
        nodeResults.Should().HaveCountLessOrEqualTo(1);
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_CAndCpp_ShouldDeduplicateGcc()
    {
        var result = await _service.CheckPrerequisitesAsync(
            [Language.C, Language.Cpp]);

        var gccResults = result.Where(r => r.BinaryName == "gcc").ToList();
        gccResults.Should().HaveCountLessOrEqualTo(1);
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_WithDotNet_ShouldNotReportMissing()
    {
        // dotnet is guaranteed to be on PATH since we're running in .NET
        var result = await _service.CheckPrerequisitesAsync([Language.CSharp]);

        result.Where(r => r.BinaryName == "dotnet").Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_MissingPrerequisite_ShouldHaveAllFields()
    {
        // Use a language that's very unlikely to be installed: let's find one that's missing
        // We just verify that any missing result has all required fields
        var languages = new[] { Language.CSharp, Language.Java, Language.Php,
            Language.Python, Language.Rust, Language.Go };

        var result = await _service.CheckPrerequisitesAsync(languages);

        foreach (var item in result)
        {
            item.BinaryName.Should().NotBeNullOrWhiteSpace();
            item.DisplayName.Should().NotBeNullOrWhiteSpace();
            item.InstallHint.Should().NotBeNullOrWhiteSpace();
            item.Language.Should().NotBe(Language.Unknown);
        }
    }

    #endregion

    #region FindBinaryOnPath

    [Theory]
    [InlineData("dotnet")]
    public void FindBinaryOnPath_WithDotNet_ShouldFind(string binary)
    {
        // dotnet must exist since we're running .NET tests
        var result = PrerequisiteService.FindBinaryOnPath(binary);

        result.Should().NotBeNull();
        File.Exists(result!).Should().BeTrue();
    }

    [Fact]
    public void FindBinaryOnPath_WithNonexistentBinary_ShouldReturnNull()
    {
        var result = PrerequisiteService.FindBinaryOnPath("this-binary-definitely-does-not-exist-xyz123");

        result.Should().BeNull();
    }

    [Fact]
    public void FindBinaryOnPath_WithEmptyName_ShouldReturnNull()
    {
        var result = PrerequisiteService.FindBinaryOnPath("");

        result.Should().BeNull();
    }

    #endregion
}
