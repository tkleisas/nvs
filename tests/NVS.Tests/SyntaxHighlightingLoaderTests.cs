using AvaloniaEdit.Highlighting;
using NVS.Core.Enums;
using NVS.Highlighting;

namespace NVS.Tests;

public sealed class SyntaxHighlightingLoaderTests : IDisposable
{
    public SyntaxHighlightingLoaderTests()
    {
        SyntaxHighlightingLoader.ClearCache();
    }

    public void Dispose()
    {
        SyntaxHighlightingLoader.ClearCache();
    }

    public static TheoryData<Language> SupportedLanguages => new()
    {
        Language.CSharp,
        Language.C,
        Language.Cpp,
        Language.JavaScript,
        Language.TypeScript,
        Language.Python,
        Language.Rust,
        Language.Go,
        Language.Json,
        Language.Xml,
        Language.Html,
        Language.Css,
        Language.Markdown,
        Language.Yaml,
        Language.Toml,
    };

    [Theory]
    [MemberData(nameof(SupportedLanguages))]
    public void GetHighlighting_WithSupportedLanguage_ShouldReturnDefinition(Language language)
    {
        var definition = SyntaxHighlightingLoader.GetHighlighting(language);

        definition.Should().NotBeNull($"language {language} should have an XSHD definition");
    }

    [Fact]
    public void GetHighlighting_WithUnknownLanguage_ShouldReturnNull()
    {
        var definition = SyntaxHighlightingLoader.GetHighlighting(Language.Unknown);

        definition.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(SupportedLanguages))]
    public void GetHighlighting_CalledTwice_ShouldReturnCachedInstance(Language language)
    {
        var first = SyntaxHighlightingLoader.GetHighlighting(language);
        var second = SyntaxHighlightingLoader.GetHighlighting(language);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void ClearCache_ShouldForceReload()
    {
        var first = SyntaxHighlightingLoader.GetHighlighting(Language.CSharp);
        SyntaxHighlightingLoader.ClearCache();
        var second = SyntaxHighlightingLoader.GetHighlighting(Language.CSharp);

        second.Should().NotBeNull();
        second.Should().NotBeSameAs(first);
    }

    [Theory]
    [MemberData(nameof(SupportedLanguages))]
    public void GetHighlighting_ShouldHaveCorrectName(Language language)
    {
        var definition = SyntaxHighlightingLoader.GetHighlighting(language);

        definition.Should().NotBeNull();
        definition!.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(Language.CSharp, "Comment")]
    [InlineData(Language.Python, "Comment")]
    [InlineData(Language.Rust, "Comment")]
    [InlineData(Language.Go, "Comment")]
    [InlineData(Language.Css, "Comment")]
    [InlineData(Language.Yaml, "Comment")]
    [InlineData(Language.Toml, "Comment")]
    public void GetHighlighting_LanguagesWithComments_ShouldHaveCommentColor(Language language, string colorName)
    {
        var definition = SyntaxHighlightingLoader.GetHighlighting(language);

        definition.Should().NotBeNull();
        var color = definition!.GetNamedColor(colorName);
        color.Should().NotBeNull($"{language} should define a '{colorName}' color");
    }

    [Theory]
    [InlineData(Language.CSharp)]
    [InlineData(Language.JavaScript)]
    [InlineData(Language.TypeScript)]
    [InlineData(Language.Python)]
    [InlineData(Language.Rust)]
    [InlineData(Language.Go)]
    public void GetHighlighting_ProgrammingLanguages_ShouldHaveKeywordColor(Language language)
    {
        var definition = SyntaxHighlightingLoader.GetHighlighting(language);

        definition.Should().NotBeNull();
        var color = definition!.GetNamedColor("Keyword");
        color.Should().NotBeNull($"{language} should define a 'Keyword' color");
    }

    [Theory]
    [InlineData(Language.CSharp)]
    [InlineData(Language.C)]
    [InlineData(Language.Cpp)]
    [InlineData(Language.JavaScript)]
    [InlineData(Language.TypeScript)]
    [InlineData(Language.Python)]
    [InlineData(Language.Rust)]
    [InlineData(Language.Go)]
    [InlineData(Language.Json)]
    public void GetHighlighting_LanguagesWithStrings_ShouldHaveStringColor(Language language)
    {
        var definition = SyntaxHighlightingLoader.GetHighlighting(language);

        definition.Should().NotBeNull();
        var color = definition!.GetNamedColor("String");
        color.Should().NotBeNull($"{language} should define a 'String' color");
    }

    [Fact]
    public void GetHighlighting_AllSupportedLanguages_ShouldHaveMainRuleSet()
    {
        foreach (var language in Enum.GetValues<Language>())
        {
            if (language == Language.Unknown) continue;

            var definition = SyntaxHighlightingLoader.GetHighlighting(language);
            definition.Should().NotBeNull($"{language} should load");
            definition!.MainRuleSet.Should().NotBeNull($"{language} should have a main rule set");
        }
    }
}
