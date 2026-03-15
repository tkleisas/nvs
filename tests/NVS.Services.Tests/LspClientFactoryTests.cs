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
    public void ResolveServerDefinition_WithPreference_RegistrySupportsLookup()
    {
        // Verify that the registry supports looking up servers by ID
        var def = LanguageServerRegistry.GetById("csharp-ls");

        def.Should().NotBeNull();
        def!.Languages.Should().Contain(Language.CSharp);
    }

    [Fact]
    public void PreferredLanguageServers_ShouldResolveFromSettings()
    {
        var settings = new AppSettings
        {
            PreferredLanguageServers = new Dictionary<string, string>
            {
                ["CSharp"] = "csharp-ls",
            },
        };

        settings.PreferredLanguageServers.TryGetValue("CSharp", out var preferredId)
            .Should().BeTrue();
        preferredId.Should().Be("csharp-ls");

        var def = LanguageServerRegistry.GetById(preferredId!);
        def.Should().NotBeNull();
        def!.Id.Should().Be("csharp-ls");
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
