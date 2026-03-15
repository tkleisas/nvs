using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Services.Lsp;

namespace NVS.Services.Tests;

public sealed class LanguageServerManagerTests
{
    private readonly LanguageServerManager _manager = new();

    [Fact]
    public void GetAvailableServers_ShouldReturnAllRegistered()
    {
        var servers = _manager.GetAvailableServers();

        servers.Should().NotBeEmpty();
        servers.Count.Should().BeGreaterOrEqualTo(13);
    }

    [Theory]
    [InlineData(Language.CSharp, "csharp-ls")]
    [InlineData(Language.Python, "pylsp")]
    [InlineData(Language.Go, "gopls")]
    [InlineData(Language.Rust, "rust-analyzer")]
    public void GetServerForLanguage_WithSupportedLanguage_ShouldReturnDefinition(
        Language language, string expectedId)
    {
        var def = _manager.GetServerForLanguage(language);

        def.Should().NotBeNull();
        def!.Id.Should().Be(expectedId);
    }

    [Fact]
    public void GetServerForLanguage_WithUnknownLanguage_ShouldReturnNull()
    {
        var def = _manager.GetServerForLanguage(Language.Unknown);

        def.Should().BeNull();
    }

    [Fact]
    public async Task CheckServerStatusAsync_WithUnknownServer_ShouldReturnUnknown()
    {
        var status = await _manager.CheckServerStatusAsync("nonexistent-server-xyz");

        status.Should().Be(LanguageServerStatus.Unknown);
    }

    [Fact]
    public async Task CheckServerStatusAsync_WithKnownServer_ShouldReturnStatus()
    {
        // dotnet is on PATH in test environments
        var status = await _manager.CheckServerStatusAsync("csharp-ls");

        // The result depends on whether csharp-ls is installed on the test machine
        status.Should().BeOneOf(
            LanguageServerStatus.Installed,
            LanguageServerStatus.NotInstalled);
    }

    [Fact]
    public void FindServerBinary_WithUnknownServer_ShouldReturnNull()
    {
        var path = _manager.FindServerBinary("nonexistent-server-xyz");

        path.Should().BeNull();
    }

    [Fact]
    public async Task InstallServerAsync_WithUnknownServer_ShouldReturnFalse()
    {
        string? lastMessage = null;
        var progress = new Progress<string>(msg => lastMessage = msg);

        var result = await _manager.InstallServerAsync("nonexistent-server", progress);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task InstallServerAsync_WithBinaryDownloadServer_ShouldReturnFalseWithMessage()
    {
        string? lastMessage = null;
        var progress = new Progress<string>(msg => lastMessage = msg);

        var result = await _manager.InstallServerAsync("clangd", progress);

        result.Should().BeFalse();
        // Allow time for Progress<T> callback
        await Task.Delay(50);
        lastMessage.Should().Contain("manually");
    }

    [Fact]
    public void FindBinaryOnPath_WithDotnet_ShouldFindIt()
    {
        // dotnet should be on PATH in any .NET test environment
        var path = LanguageServerManager.FindBinaryOnPath("dotnet");

        path.Should().NotBeNull();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void FindBinaryOnPath_WithNonexistentBinary_ShouldReturnNull()
    {
        var path = LanguageServerManager.FindBinaryOnPath("this-binary-does-not-exist-xyzzy");

        path.Should().BeNull();
    }

    [Fact]
    public void GetCurrentRid_ShouldReturnValidRid()
    {
        var rid = LanguageServerManager.GetCurrentRid();

        rid.Should().NotBeNull();
        rid.Should().MatchRegex(@"^(win|linux|osx)-(x64|arm64)$");
    }

    [Fact]
    public void GetNvsToolsDir_ShouldReturnAppDataPath()
    {
        var dir = LanguageServerManager.GetNvsToolsDir("omnisharp");

        dir.Should().Contain("NVS");
        dir.Should().Contain("tools");
        dir.Should().Contain("omnisharp");
    }

    [Fact]
    public void FindInNvsTools_WithNonexistentDir_ShouldReturnNull()
    {
        var path = LanguageServerManager.FindInNvsTools("nonexistent-server-xyz", "binary");

        path.Should().BeNull();
    }

    [Fact]
    public async Task CheckServerStatusAsync_WithOmniSharp_ShouldReturnStatus()
    {
        var status = await _manager.CheckServerStatusAsync("omnisharp");

        // OmniSharp probably isn't installed in test environment
        status.Should().BeOneOf(
            LanguageServerStatus.Installed,
            LanguageServerStatus.NotInstalled);
    }
}
