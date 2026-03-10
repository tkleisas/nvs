using FluentAssertions;
using NVS.Core.Enums;
using NVS.Services.Languages;
using Xunit;

namespace NVS.Services.Tests;

public class LanguageServiceTests
{
    private readonly LanguageService _service = new();

    [Theory]
    [InlineData("file.cs", Language.CSharp)]
    [InlineData("file.cpp", Language.Cpp)]
    [InlineData("file.c", Language.C)]
    [InlineData("file.js", Language.JavaScript)]
    [InlineData("file.ts", Language.TypeScript)]
    [InlineData("file.py", Language.Python)]
    [InlineData("file.rs", Language.Rust)]
    [InlineData("file.go", Language.Go)]
    [InlineData("file.json", Language.Json)]
    [InlineData("file.xml", Language.Xml)]
    [InlineData("file.md", Language.Markdown)]
    [InlineData("file.yaml", Language.Yaml)]
    [InlineData("file.html", Language.Html)]
    [InlineData("file.css", Language.Css)]
    [InlineData("file.unknown", Language.Unknown)]
    public void DetectLanguage_ShouldReturnCorrectLanguage(string filePath, Language expected)
    {
        var result = _service.DetectLanguage(filePath);

        result.Should().Be(expected);
    }

    [Fact]
    public void GetLanguageId_ShouldReturnCorrectId()
    {
        _service.GetLanguageId(Language.CSharp).Should().Be("csharp");
        _service.GetLanguageId(Language.TypeScript).Should().Be("typescript");
        _service.GetLanguageId(Language.Unknown).Should().Be("plaintext");
    }

    [Fact]
    public void GetLanguageFromId_ShouldReturnCorrectLanguage()
    {
        _service.GetLanguageFromId("csharp").Should().Be(Language.CSharp);
        _service.GetLanguageFromId("typescript").Should().Be(Language.TypeScript);
        _service.GetLanguageFromId("plaintext").Should().Be(Language.Unknown);
        _service.GetLanguageFromId("unknown").Should().Be(Language.Unknown);
    }

    [Fact]
    public void GetFileExtensions_ShouldReturnExtensionsForKnownLanguage()
    {
        var extensions = _service.GetFileExtensions(Language.CSharp);

        extensions.Should().Contain(".cs");
        extensions.Should().Contain(".csx");
    }

    [Fact]
    public void GetFileExtensions_ShouldReturnEmptyForUnknownLanguage()
    {
        var extensions = _service.GetFileExtensions(Language.Unknown);

        extensions.Should().BeEmpty();
    }

    [Fact]
    public void GetLanguageServer_ShouldReturnServerForSupportedLanguage()
    {
        _service.GetLanguageServer(Language.CSharp).Should().Be("omnisharp");
        _service.GetLanguageServer(Language.TypeScript).Should().Be("typescript-language-server");
        _service.GetLanguageServer(Language.Python).Should().Be("pyright");
    }

    [Fact]
    public void GetLanguageServer_ShouldReturnNullForUnsupportedLanguage()
    {
        _service.GetLanguageServer(Language.Json).Should().BeNull();
        _service.GetLanguageServer(Language.Unknown).Should().BeNull();
    }
}
