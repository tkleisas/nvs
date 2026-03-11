using NVS.Core.Enums;
using NVS.Core.Models.Settings;
using NVS.Services.Lsp;

namespace NVS.Services.Tests;

public sealed class LanguageServerRegistryTests
{
    [Fact]
    public void GetAll_ShouldReturnAllRegisteredServers()
    {
        var servers = LanguageServerRegistry.GetAll();

        servers.Should().NotBeEmpty();
        servers.Count.Should().BeGreaterOrEqualTo(12);
    }

    [Fact]
    public void GetAll_ShouldContainUniqueIds()
    {
        var servers = LanguageServerRegistry.GetAll();
        var ids = servers.Select(s => s.Id).ToList();

        ids.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("csharp-ls")]
    [InlineData("clangd")]
    [InlineData("typescript-language-server")]
    [InlineData("pylsp")]
    [InlineData("rust-analyzer")]
    [InlineData("gopls")]
    [InlineData("vscode-json-language-server")]
    [InlineData("vscode-html-language-server")]
    [InlineData("vscode-css-language-server")]
    [InlineData("yaml-language-server")]
    [InlineData("marksman")]
    [InlineData("taplo")]
    public void GetById_WithValidId_ShouldReturnDefinition(string serverId)
    {
        var def = LanguageServerRegistry.GetById(serverId);

        def.Should().NotBeNull();
        def!.Id.Should().Be(serverId);
        def.Name.Should().NotBeNullOrWhiteSpace();
        def.License.Should().NotBeNullOrWhiteSpace();
        def.BinaryName.Should().NotBeNullOrWhiteSpace();
        def.Languages.Should().NotBeEmpty();
    }

    [Fact]
    public void GetById_WithInvalidId_ShouldReturnNull()
    {
        var def = LanguageServerRegistry.GetById("nonexistent-server");

        def.Should().BeNull();
    }

    [Theory]
    [InlineData(Language.CSharp, "csharp-ls")]
    [InlineData(Language.C, "clangd")]
    [InlineData(Language.Cpp, "clangd")]
    [InlineData(Language.TypeScript, "typescript-language-server")]
    [InlineData(Language.JavaScript, "typescript-language-server")]
    [InlineData(Language.Python, "pylsp")]
    [InlineData(Language.Rust, "rust-analyzer")]
    [InlineData(Language.Go, "gopls")]
    [InlineData(Language.Json, "vscode-json-language-server")]
    [InlineData(Language.Html, "vscode-html-language-server")]
    [InlineData(Language.Css, "vscode-css-language-server")]
    [InlineData(Language.Yaml, "yaml-language-server")]
    [InlineData(Language.Markdown, "marksman")]
    [InlineData(Language.Toml, "taplo")]
    public void GetForLanguage_WithSupportedLanguage_ShouldReturnCorrectServer(
        Language language, string expectedServerId)
    {
        var def = LanguageServerRegistry.GetForLanguage(language);

        def.Should().NotBeNull();
        def!.Id.Should().Be(expectedServerId);
    }

    [Fact]
    public void GetForLanguage_WithUnsupportedLanguage_ShouldReturnNull()
    {
        var def = LanguageServerRegistry.GetForLanguage(Language.Unknown);

        def.Should().BeNull();
    }

    [Theory]
    [InlineData("csharp-ls", InstallMethod.DotnetTool)]
    [InlineData("clangd", InstallMethod.BinaryDownload)]
    [InlineData("typescript-language-server", InstallMethod.Npm)]
    [InlineData("pylsp", InstallMethod.Pip)]
    [InlineData("rust-analyzer", InstallMethod.BinaryDownload)]
    [InlineData("gopls", InstallMethod.GoInstall)]
    [InlineData("taplo", InstallMethod.Cargo)]
    public void GetById_ShouldHaveCorrectInstallMethod(string serverId, InstallMethod expected)
    {
        var def = LanguageServerRegistry.GetById(serverId);

        def.Should().NotBeNull();
        def!.InstallMethod.Should().Be(expected);
    }

    [Fact]
    public void AllServers_ShouldHaveHomepageUrl()
    {
        var servers = LanguageServerRegistry.GetAll();

        foreach (var server in servers)
        {
            server.HomepageUrl.Should().NotBeNullOrWhiteSpace(
                $"Server {server.Id} should have a homepage URL");
        }
    }

    [Fact]
    public void AllServers_ShouldHaveNonEmptyDescription()
    {
        var servers = LanguageServerRegistry.GetAll();

        foreach (var server in servers)
        {
            server.Description.Should().NotBeNullOrWhiteSpace(
                $"Server {server.Id} should have a description");
        }
    }

    [Fact]
    public void AllNonBinaryServers_ShouldHaveInstallCommand()
    {
        var servers = LanguageServerRegistry.GetAll()
            .Where(s => s.InstallMethod != InstallMethod.BinaryDownload);

        foreach (var server in servers)
        {
            server.InstallCommand.Should().NotBeNullOrWhiteSpace(
                $"Server {server.Id} ({server.InstallMethod}) should have an install command");
            server.InstallPackage.Should().NotBeNullOrWhiteSpace(
                $"Server {server.Id} ({server.InstallMethod}) should have an install package");
        }
    }

    [Fact]
    public void All_Dictionary_ShouldReturnReadOnlyView()
    {
        var all = LanguageServerRegistry.All;

        all.Should().NotBeEmpty();
        all.Should().ContainKey("clangd");
        all.Should().ContainKey("gopls");
    }
}
