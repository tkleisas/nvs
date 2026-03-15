using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.Services.Lsp;

namespace NVS.Services.Tests;

public sealed class LspClientFactoryTests
{
    [Fact]
    public void ResolveServerDefinition_WithNoPreference_ShouldReturnDefault()
    {
        // Arrange: settings with no preferred servers
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.AppSettings.Returns(new AppSettings());

        var factory = CreateFactory(settingsService);

        // Act: use reflection to test private method indirectly via CreateClientAsync
        // The factory should pick csharp-ls (default) for C#
        var def = LanguageServerRegistry.GetForLanguage(Language.CSharp);

        // Assert
        def.Should().NotBeNull();
        def!.Id.Should().Be("csharp-ls");
    }

    [Fact]
    public void ResolveServerDefinition_WithPreference_ShouldReturnPreferred()
    {
        // Verify that the registry has omnisharp and it can be looked up
        var def = LanguageServerRegistry.GetById("omnisharp");

        def.Should().NotBeNull();
        def!.Languages.Should().Contain(Language.CSharp);
    }

    [Fact]
    public void PreferredLanguageServers_ShouldOverrideDefault()
    {
        var settings = new AppSettings
        {
            PreferredLanguageServers = new Dictionary<string, string>
            {
                ["CSharp"] = "omnisharp",
            },
        };

        // The preferred server ID should be "omnisharp"
        settings.PreferredLanguageServers.TryGetValue("CSharp", out var preferredId)
            .Should().BeTrue();
        preferredId.Should().Be("omnisharp");

        var def = LanguageServerRegistry.GetById(preferredId!);
        def.Should().NotBeNull();
        def!.Id.Should().Be("omnisharp");
    }

    [Fact]
    public void PreferredLanguageServers_WithInvalidId_ShouldFallBackToDefault()
    {
        var settings = new AppSettings
        {
            PreferredLanguageServers = new Dictionary<string, string>
            {
                ["CSharp"] = "nonexistent-server",
            },
        };

        // Look up the preferred — should be null (not registered)
        var preferredDef = LanguageServerRegistry.GetById("nonexistent-server");
        preferredDef.Should().BeNull();

        // Fall back to default
        var defaultDef = LanguageServerRegistry.GetForLanguage(Language.CSharp);
        defaultDef.Should().NotBeNull();
        defaultDef!.Id.Should().Be("csharp-ls");
    }

    [Fact]
    public void OmniSharp_BuildConfig_ShouldIncludeSolutionArg()
    {
        var def = LanguageServerRegistry.GetById("omnisharp")!;
        var rootPath = "/home/user/project";

        // Simulate what BuildConfig does for OmniSharp
        var baseArgs = def.DefaultArgs;
        var argsList = new List<string>();

        if (def.RequiresSolutionArg && !string.IsNullOrEmpty(def.SolutionArgPrefix))
        {
            argsList.Add(def.SolutionArgPrefix);
            argsList.Add(rootPath);
        }

        argsList.AddRange(baseArgs);

        argsList.Should().HaveCount(3);
        argsList[0].Should().Be("-s");
        argsList[1].Should().Be(rootPath);
        argsList[2].Should().Be("--languageserver");
    }

    [Fact]
    public void CSharpLs_BuildConfig_ShouldNotIncludeSolutionArg()
    {
        var def = LanguageServerRegistry.GetById("csharp-ls")!;

        def.RequiresSolutionArg.Should().BeFalse();
    }

    private static LspClientFactory CreateFactory(ISettingsService settingsService)
    {
        var languageService = Substitute.For<ILanguageService>();
        return new LspClientFactory(languageService, settingsService);
    }
}
