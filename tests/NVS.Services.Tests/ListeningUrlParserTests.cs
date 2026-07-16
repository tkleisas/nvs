using NVS.Services.Launch;

namespace NVS.Services.Tests;

public sealed class ListeningUrlParserTests
{
    [Fact]
    public void TryExtract_KestrelLine_ReturnsUrl()
    {
        var url = ListeningUrlParser.TryExtract("      Now listening on: http://localhost:5000");
        url.Should().Be("http://localhost:5000");
    }

    [Fact]
    public void TryExtract_Https_ReturnsUrl()
    {
        var url = ListeningUrlParser.TryExtract("Now listening on: https://localhost:5001");
        url.Should().Be("https://localhost:5001");
    }

    [Fact]
    public void TryExtract_EmptyOrNoMatch_ReturnsNull()
    {
        ListeningUrlParser.TryExtract(null).Should().BeNull();
        ListeningUrlParser.TryExtract("").Should().BeNull();
        ListeningUrlParser.TryExtract("some random output").Should().BeNull();
    }

    [Fact]
    public void TryExtract_LastMatchWinsWhenMultiple()
    {
        var text = """
        Now listening on: http://localhost:5000
        Now listening on: https://localhost:5001
        """;
        ListeningUrlParser.TryExtract(text).Should().Be("https://localhost:5001");
    }

    [Fact]
    public void TryExtract_TrimsTrailingPunctuation()
    {
        var url = ListeningUrlParser.TryExtract("Now listening on: http://localhost:5000.");
        url.Should().Be("http://localhost:5000");
    }

    [Theory]
    [InlineData("dotnet run\r\n info: ...[14]\r\n      Now listening on: http://localhost:5062\r\n")]
    [InlineData("Now listening on: https://[::]:5171")]
    public void TryExtract_RealisticOutput_ReturnsUrl(string text)
    {
        ListeningUrlParser.TryExtract(text).Should().StartWith("http");
    }
}

public sealed class ListeningUrlWatcherTests
{
    [Fact]
    public void Append_AcrossChunks_FiresOnce()
    {
        var watcher = new ListeningUrlWatcher();
        var detected = new List<string>();
        watcher.UrlDetected += url => detected.Add(url);

        var fired = watcher.Append("info: Microsoft.Hosting.Lifetime[14]\n      Now list");
        fired.Should().BeFalse();
        detected.Should().BeEmpty();

        fired = watcher.Append("ening on: http://localhost:5000\nApplication started.");
        fired.Should().BeTrue();
        detected.Should().ContainSingle().Which.Should().Be("http://localhost:5000");
    }

    [Fact]
    public void Append_FiresOnlyOnceEvenWithMoreOutput()
    {
        var watcher = new ListeningUrlWatcher();
        var count = 0;
        watcher.UrlDetected += _ => count++;

        watcher.Append("Now listening on: http://localhost:5000\n");
        watcher.Append("Now listening on: https://localhost:5001\n");

        count.Should().Be(1);
    }

    [Fact]
    public void Reset_AllowsReDetection()
    {
        var watcher = new ListeningUrlWatcher();
        var count = 0;
        watcher.UrlDetected += _ => count++;

        watcher.Append("Now listening on: http://localhost:5000\n");
        watcher.Reset();
        watcher.Append("Now listening on: https://localhost:5001\n");

        count.Should().Be(2);
    }

    [Fact]
    public void Append_EmptyOrNoMatch_DoesNotFire()
    {
        var watcher = new ListeningUrlWatcher();
        var fired = false;
        watcher.UrlDetected += _ => fired = true;

        watcher.Append("");
        watcher.Append("Building...\nBuild succeeded.\n");

        fired.Should().BeFalse();
    }

    [Fact]
    public void Append_CRLFLineSeparators_Detected()
    {
        var watcher = new ListeningUrlWatcher();
        string? detected = null;
        watcher.UrlDetected += url => detected = url;

        watcher.Append("Now listening on: http://localhost:5000\r\n");

        detected.Should().Be("http://localhost:5000");
    }
}