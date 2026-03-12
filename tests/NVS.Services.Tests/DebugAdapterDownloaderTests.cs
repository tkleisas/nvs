using NVS.Services.Debug;

namespace NVS.Services.Tests;

public sealed class DebugAdapterDownloaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DebugAdapterDownloader _downloader;

    public DebugAdapterDownloaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nvs-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _downloader = new DebugAdapterDownloader(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── GetInstalledPath ──────────────────────────────────────────

    [Fact]
    public void GetInstalledPath_WhenNotInstalled_ShouldReturnNull()
    {
        var result = _downloader.GetInstalledPath("netcoredbg");

        result.Should().BeNull();
    }

    [Fact]
    public void GetInstalledPath_WhenExecutableExists_ShouldReturnPath()
    {
        // Simulate an installed adapter
        var adapterDir = Path.Combine(_tempDir, "netcoredbg");
        Directory.CreateDirectory(adapterDir);

        var exeName = OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg";
        var exePath = Path.Combine(adapterDir, exeName);
        File.WriteAllText(exePath, "fake");

        var result = _downloader.GetInstalledPath("netcoredbg");

        result.Should().Be(exePath);
    }

    // ── ToolsDirectory ───────────────────────────────────────────

    [Fact]
    public void ToolsDirectory_ShouldReturnConfiguredPath()
    {
        _downloader.ToolsDirectory.Should().Be(_tempDir);
    }

    [Fact]
    public void DefaultConstructor_ShouldUseNvsToolsDir()
    {
        var downloader = new DebugAdapterDownloader();
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nvs", "tools");

        downloader.ToolsDirectory.Should().Be(expected);
    }

    // ── GetNetcoredbgDownloadUrl ────────────────────────────────

    [Fact]
    public void GetNetcoredbgDownloadUrl_ShouldReturnValidUrl()
    {
        var (url, ext) = DebugAdapterDownloader.GetNetcoredbgDownloadUrl();

        url.Should().Contain("github.com/Samsung/netcoredbg/releases");
        url.Should().Contain("netcoredbg-");

        if (OperatingSystem.IsWindows())
        {
            url.Should().Contain("win64.zip");
            ext.Should().Be(".zip");
        }
        else if (OperatingSystem.IsMacOS())
        {
            url.Should().Contain("osx-amd64.tar.gz");
            ext.Should().Be(".tar.gz");
        }
        else
        {
            url.Should().Contain("linux-");
            ext.Should().Be(".tar.gz");
        }
    }

    // ── StripFirstDirectory ─────────────────────────────────────

    [Theory]
    [InlineData("netcoredbg/foo.dll", "foo.dll")]
    [InlineData("netcoredbg/subdir/bar.dll", "subdir/bar.dll")]
    [InlineData("netcoredbg\\foo.dll", "foo.dll")]
    [InlineData("single-name", "single-name")]
    [InlineData("dir/", "")]
    public void StripFirstDirectory_ShouldStripCorrectly(string input, string expected)
    {
        var result = DebugAdapterDownloader.StripFirstDirectory(input);

        result.Should().Be(expected);
    }

    // ── Registry Integration ────────────────────────────────────

    [Fact]
    public void Registry_FindAdapterExecutable_ShouldCheckToolsDir()
    {
        var registry = new DebugAdapterRegistry(_downloader);

        // Not installed yet
        var result = registry.FindAdapterExecutable("coreclr");
        // Might be found on PATH or not; but let's test with a fake installed one

        // Simulate installed adapter
        var adapterDir = Path.Combine(_tempDir, "netcoredbg");
        Directory.CreateDirectory(adapterDir);
        var exeName = OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg";
        File.WriteAllText(Path.Combine(adapterDir, exeName), "fake");

        result = registry.FindAdapterExecutable("coreclr");

        // Should find it (could also be on PATH, but at minimum our tools dir should work)
        result.Should().NotBeNull();
        result.Should().Contain("netcoredbg");
    }
}
