using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using NVS.Automation;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;

namespace NVS.Tests;

public class AutomationServerTests
{
    private sealed class FakeHost : IAutomationHost
    {
        public (string Path, string? Control)? LastScreenshot;

        public Task<object> PingAsync() => Task.FromResult<object>(new Dictionary<string, object?> { ["app"] = "NVS" });
        public Task<object> GetStateAsync() => Task.FromResult<object>(new Dictionary<string, object?>());
        public Task<object> GetTreeAsync(int maxDepth, int maxNodes) =>
            Task.FromResult<object>(new Dictionary<string, object?> { ["maxDepth"] = maxDepth, ["maxNodes"] = maxNodes });
        public Task<object> ScreenshotAsync(string path, string? controlId)
        {
            LastScreenshot = (path, controlId);
            return Task.FromResult<object>(new Dictionary<string, object?> { ["path"] = path });
        }
        public Task<object> ScreenshotWindowAsync(string path, string title) =>
            Task.FromResult<object>(new Dictionary<string, object?> { ["path"] = path });
        public Task<object> InvokeCommandAsync(string name) => Task.FromResult<object>(new Dictionary<string, object?> { ["invoked"] = name });
        public Task<object> InvokeMenuAsync(string path) => Task.FromResult<object>(new Dictionary<string, object?> { ["invoked"] = path });
        public Task<object> SetTextAsync(string controlId, string text) => Task.FromResult<object>(new Dictionary<string, object?> { ["control"] = controlId });
        public Task<object> OpenSolutionAsync(string path) => Task.FromResult<object>(new Dictionary<string, object?> { ["opened"] = path });
        public Task<object> ActivateAsync(string id) => Task.FromResult<object>(new Dictionary<string, object?> { ["activated"] = id });
    }

    private static async Task<string> RoundTripAsync(AutomationServer server, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", server.Port);
        await using var stream = client.GetStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await writer.WriteLineAsync(request);
        return (await reader.ReadLineAsync())!;
    }

    [Fact]
    public async Task Ping_OverRealSocket_ReturnsOkResult()
    {
        using var server = new AutomationServer(new FakeHost(), 0);
        server.Start();

        var response = await RoundTripAsync(server, """{"id":7,"cmd":"ping"}""");

        using var doc = JsonDocument.Parse(response);
        doc.RootElement.GetProperty("id").GetInt64().Should().Be(7);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("result").GetProperty("app").GetString().Should().Be("NVS");
    }

    [Fact]
    public async Task Screenshot_OverRealSocket_ForwardsArgumentsToHost()
    {
        var host = new FakeHost();
        using var server = new AutomationServer(host, 0);
        server.Start();

        var response = await RoundTripAsync(server,
            """{"id":1,"cmd":"screenshot","args":{"path":"C:/tmp/a.png","control":"DatabaseTreeView"}}""");

        using var doc = JsonDocument.Parse(response);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        host.LastScreenshot.Should().Be(("C:/tmp/a.png", "DatabaseTreeView"));
    }

    [Fact]
    public async Task UnknownCommand_ReturnsErrorWithKnownCommands()
    {
        using var server = new AutomationServer(new FakeHost(), 0);
        server.Start();

        var response = await RoundTripAsync(server, """{"id":2,"cmd":"explode"}""");

        using var doc = JsonDocument.Parse(response);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("unknown cmd");
    }

    [Fact]
    public async Task MalformedJson_ReturnsError()
    {
        using var server = new AutomationServer(new FakeHost(), 0);
        server.Start();

        var response = await RoundTripAsync(server, "{ not json");

        using var doc = JsonDocument.Parse(response);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("invalid JSON");
    }

    [Fact]
    public async Task Tree_ForwardsDepthAndNodeCaps()
    {
        using var server = new AutomationServer(new FakeHost(), 0);
        server.Start();

        var response = await RoundTripAsync(server, """{"id":3,"cmd":"tree","args":{"maxDepth":4,"maxNodes":50}}""");

        using var doc = JsonDocument.Parse(response);
        var result = doc.RootElement.GetProperty("result");
        result.GetProperty("maxDepth").GetInt32().Should().Be(4);
        result.GetProperty("maxNodes").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task MissingRequiredArg_ReturnsHelpfulError()
    {
        using var server = new AutomationServer(new FakeHost(), 0);
        server.Start();

        var response = await RoundTripAsync(server, """{"id":4,"cmd":"screenshot"}""");

        using var doc = JsonDocument.Parse(response);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("args.path");
    }
}

public class CommandInvokerTests
{
    private static MainViewModel CreateMainVm()
    {
        var workspaceService = Substitute.For<IWorkspaceService>();
        var editorService = Substitute.For<IEditorService>();
        var fs = Substitute.For<IFileSystemService>();
        var editor = new EditorViewModel(editorService, fs);
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());
        return new MainViewModel(workspaceService, editorService, fs, editor,
            Substitute.For<IGitService>(), Substitute.For<ITerminalService>(), settings,
            Substitute.For<ISolutionService>(), Substitute.For<IBuildService>());
    }

    [Fact]
    public void TryInvoke_ByNameWithoutSuffix_ExecutesCommand()
    {
        var vm = CreateMainVm();

        CommandInvoker.TryInvoke(vm, "ShowSearch", out var message).Should().BeTrue(message);

        vm.SidebarMode.Should().Be("Search");
    }

    [Fact]
    public void TryInvoke_ByFullCommandPropertyName_Executes()
    {
        var vm = CreateMainVm();

        CommandInvoker.TryInvoke(vm, "ShowExplorerCommand", out _).Should().BeTrue();

        vm.SidebarMode.Should().Be("Explorer");
    }

    [Fact]
    public void TryInvoke_UnknownName_ReturnsFalse()
    {
        var vm = CreateMainVm();

        CommandInvoker.TryInvoke(vm, "Nonsense", out var message).Should().BeFalse();
        message.Should().Contain("Nonsense");
    }
}

public class MenuItemMatcherTests
{
    private static MenuItem BuildMenu()
    {
        var ask = new MenuItem { Header = "_Ask AI..." };
        var database = new MenuItem { Header = "_Database" };
        database.Items.Add(ask);
        database.Items.Add(new MenuItem { Header = "_Reports..." });

        var file = new MenuItem { Header = "_File" };
        file.Items.Add(new MenuItem { Header = "E_xit" });

        var root = new MenuItem { Header = "Root" };
        root.Items.Add(file);
        root.Items.Add(database);
        return root;
    }

    [Fact]
    public void Find_TwoLevelPath_IgnoresAccessKeys()
    {
        var root = BuildMenu();

        var found = MenuItemMatcher.Find(root.Items, "database/ask ai...");

        found.Should().NotBeNull();
        found!.Header.Should().Be("_Ask AI...");
    }

    [Fact]
    public void Find_TopLevel_ReturnsParent()
    {
        var root = BuildMenu();

        MenuItemMatcher.Find(root.Items, "File").Should().NotBeNull();
    }

    [Fact]
    public void Find_NoMatch_ReturnsNull()
    {
        var root = BuildMenu();

        MenuItemMatcher.Find(root.Items, "Database/Nope").Should().BeNull();
        MenuItemMatcher.Find(root.Items, "Missing").Should().BeNull();
    }
}

public class ParseAutomationPortTests
{
    [Fact]
    public void InlineForm_Parses()
    {
        App.ParseAutomationPort(["--automation-port=5050"]).Should().Be(5050);
    }

    [Fact]
    public void SeparateForm_Parses()
    {
        App.ParseAutomationPort(["--automation-port", "5051"]).Should().Be(5051);
    }

    [Fact]
    public void NoFlag_ReturnsNull()
    {
        App.ParseAutomationPort(["some.slnx"]).Should().BeNull();
    }

    [Fact]
    public void InvalidPort_ReturnsNull()
    {
        App.ParseAutomationPort(["--automation-port=abc"]).Should().BeNull();
        App.ParseAutomationPort(["--automation-port=-5"]).Should().BeNull();
    }

    [Fact]
    public void FirstPositionalArg_SkipsAutomationFlagAndValue()
    {
        App.FirstPositionalArg(["--automation-port", "5050", "some.slnx"]).Should().Be("some.slnx");
    }

    [Fact]
    public void FirstPositionalArg_SkipsInlineFlag()
    {
        App.FirstPositionalArg(["--automation-port=5050", "some.slnx"]).Should().Be("some.slnx");
    }

    [Fact]
    public void FirstPositionalArg_NoPositional_ReturnsNull()
    {
        App.FirstPositionalArg(["--automation-port", "5050"]).Should().BeNull();
        App.FirstPositionalArg([]).Should().BeNull();
    }
}
