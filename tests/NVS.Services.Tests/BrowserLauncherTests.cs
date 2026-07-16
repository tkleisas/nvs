using NVS.Services.Launch;

namespace NVS.Services.Tests;

public sealed class BrowserLauncherTests
{
    [Fact]
    public void ResolveOpenCommand_OnMac_UsesOpen()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var (file, args) = BrowserLauncher.ResolveOpenCommand("https://example.com/path?q=1");
        file.Should().Be("open");
        args.Should().Contain("https://example.com/path?q=1");
    }

    [Fact]
    public void ResolveOpenCommand_OnLinux_UsesXdgOpen()
    {
        if (!OperatingSystem.IsLinux()) return;
        var (file, args) = BrowserLauncher.ResolveOpenCommand("https://example.com/path?q=1");
        file.Should().Be("xdg-open");
        args.Should().Contain("https://example.com/path?q=1");
    }

    [Fact]
    public void ResolveOpenCommand_EscapesEmbeddedQuotes()
    {
        if (OperatingSystem.IsWindows()) return; // Windows doesn't use this code path anymore
        // A URL containing a quote must be escaped so the shell doesn't terminate the arg early.
        var (_, args) = BrowserLauncher.ResolveOpenCommand("https://example.com/\"bad\"");
        args.Should().Contain("\\\"bad\\\"");
    }
}