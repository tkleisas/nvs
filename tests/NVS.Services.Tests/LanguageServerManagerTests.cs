using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
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
        servers.Count.Should().BeGreaterOrEqualTo(12);
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
        var dir = LanguageServerManager.GetNvsToolsDir("csharp-ls");

        dir.Should().Contain("NVS");
        dir.Should().Contain("tools");
        dir.Should().Contain("csharp-ls");
    }

    [Fact]
    public void FindInNvsTools_WithNonexistentDir_ShouldReturnNull()
    {
        var path = LanguageServerManager.FindInNvsTools("nonexistent-server-xyz", "binary");

        path.Should().BeNull();
    }

}

public sealed class LanguageServerDownloadUrlTests
{
    private static LanguageServerDefinition Def(string template, string version = "1.0.0") => new()
    {
        Id = "test-server",
        Name = "Test",
        Description = "Test server",
        License = "MIT",
        Languages = [],
        BinaryName = "test",
        InstallMethod = InstallMethod.BinaryDownload,
        DownloadUrlTemplate = template,
        Version = version,
    };

    [Fact]
    public void ResolveDownloadUrl_PlaceholderTemplate_SubstitutesVersionAndRid()
    {
        var (url, _) = LanguageServerManager.ResolveDownloadUrl(
            Def("https://example.com/{version}/binary-{rid}.{ext}", "2.3.4"), "linux-x64");

        var expectedExt = OperatingSystem.IsWindows() ? "zip" : "tar.gz";
        url.Should().Be($"https://example.com/2.3.4/binary-linux-x64.{expectedExt}");
    }

    [Fact]
    public void ResolveDownloadUrl_StaticTarGzUrl_DerivesTarGzOnAllPlatforms()
    {
        // jdtls ships only .tar.gz — no {ext} placeholder needed, format comes from the URL
        var (_, ext) = LanguageServerManager.ResolveDownloadUrl(
            Def("https://example.com/jdt-language-server-1.57.0-202602261110.tar.gz"), "win-x64");

        ext.Should().Be("tar.gz");
    }

    [Fact]
    public void ResolveDownloadUrl_StaticZipUrl_DerivesZip()
    {
        var (_, ext) = LanguageServerManager.ResolveDownloadUrl(
            Def("https://example.com/server-win64.zip"), "win-x64");

        ext.Should().Be("zip");
    }

    [Fact]
    public void JdtlsEntry_HasDownloadUrlAndVersion()
    {
        var def = LanguageServerRegistry.GetById("jdtls");

        def.Should().NotBeNull();
        def!.Version.Should().NotBeNullOrWhiteSpace();
        def.DownloadUrlTemplate.Should().StartWith("https://");
        def.InstallMethod.Should().Be(InstallMethod.BinaryDownload);
    }

    [Fact]
    public void JdtlsLauncherContent_Cmd_ResolvesJdkAndLauncherJar()
    {
        LanguageServerManager.JdtlsCmdContent.Should().Contain("org.eclipse.equinox.launcher_*.jar");
        LanguageServerManager.JdtlsCmdContent.Should().Contain("config_win");
        LanguageServerManager.JdtlsCmdContent.Should().Contain("JAVA_HOME");
    }

    [Fact]
    public void JdtlsLauncherContent_Shell_ResolvesJdkAndLauncherJar()
    {
        LanguageServerManager.JdtlsShContent.Should().Contain("org.eclipse.equinox.launcher_*.jar");
        LanguageServerManager.JdtlsShContent.Should().Contain("config_linux");
        LanguageServerManager.JdtlsShContent.Should().Contain("JAVA_HOME");
    }
}
