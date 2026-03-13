using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class LlmChatToolViewModelTests
{
    [Fact]
    public void Constructor_ShouldSetIdAndTitle()
    {
        var vm = CreateViewModel();

        vm.Id.Should().Be("LlmChat");
        vm.Title.Should().Contain("Chat");
    }

    [Fact]
    public void UserInput_WhenSet_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LlmChatToolViewModel.UserInput))
                raised = true;
        };

        vm.UserInput = "hello";

        raised.Should().BeTrue();
        vm.UserInput.Should().Be("hello");
    }

    [Fact]
    public void SelectedTaskMode_DefaultShouldBeGeneral()
    {
        var vm = CreateViewModel();

        vm.SelectedTaskMode.Should().Be("general");
    }

    [Fact]
    public void SelectedTaskMode_WhenChanged_ShouldRaisePropertyChanged()
    {
        var vm = CreateViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LlmChatToolViewModel.SelectedTaskMode))
                raised = true;
        };

        vm.SelectedTaskMode = "coding";

        raised.Should().BeTrue();
        vm.SelectedTaskMode.Should().Be("coding");
    }

    [Fact]
    public void TaskModes_ShouldContainAllModes()
    {
        LlmChatToolViewModel.TaskModes.Should().BeEquivalentTo(
            new[] { "general", "coding", "debugging", "testing" });
    }

    [Fact]
    public void Messages_ShouldStartEmpty()
    {
        var vm = CreateViewModel();

        vm.Messages.Should().BeEmpty();
    }

    [Fact]
    public void IsSending_DefaultShouldBeFalse()
    {
        var vm = CreateViewModel();

        vm.IsSending.Should().BeFalse();
    }

    [Fact]
    public void StatusText_DefaultShouldBeReady()
    {
        var vm = CreateViewModel();

        vm.StatusText.Should().Be("Ready");
    }

    [Fact]
    public void ClearChatCommand_ShouldClearMessages()
    {
        var vm = CreateViewModel();
        vm.Messages.Add(new ChatBubble("user", "hello"));
        vm.Messages.Add(new ChatBubble("assistant", "world"));

        vm.ClearChatCommand.Execute(null);

        vm.Messages.Should().BeEmpty();
    }

    [Fact]
    public void ClearChatCommand_ShouldResetStatus()
    {
        var vm = CreateViewModel();
        vm.Messages.Add(new ChatBubble("user", "test"));

        vm.ClearChatCommand.Execute(null);

        vm.StatusText.Should().Be("Ready");
    }

    [Fact]
    public void SendMessageCommand_WhenEmptyInput_CannotExecute()
    {
        var vm = CreateViewModel();
        vm.UserInput = "";

        vm.SendMessageCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SendMessageCommand_WhenHasInput_CanExecute()
    {
        var vm = CreateViewModel();
        vm.UserInput = "hello";

        vm.SendMessageCommand.CanExecute(null).Should().BeTrue();
    }

    // --- ChatBubble Tests ---

    [Fact]
    public void ChatBubble_UserRole_ShouldSetIsUser()
    {
        var bubble = new ChatBubble("user", "hello");

        bubble.IsUser.Should().BeTrue();
        bubble.IsAssistant.Should().BeFalse();
        bubble.IsSystem.Should().BeFalse();
        bubble.IsTool.Should().BeFalse();
    }

    [Fact]
    public void ChatBubble_AssistantRole_ShouldSetIsAssistant()
    {
        var bubble = new ChatBubble("assistant", "response");

        bubble.IsAssistant.Should().BeTrue();
        bubble.IsUser.Should().BeFalse();
    }

    [Fact]
    public void ChatBubble_SystemRole_ShouldSetIsSystem()
    {
        var bubble = new ChatBubble("system", "notice");

        bubble.IsSystem.Should().BeTrue();
    }

    [Fact]
    public void ChatBubble_ToolRole_ShouldSetIsTool()
    {
        var bubble = new ChatBubble("tool", "⚡ read_file: ✓");

        bubble.IsTool.Should().BeTrue();
    }

    [Fact]
    public void ChatBubble_AppendContent_ShouldStreamTokens()
    {
        var bubble = new ChatBubble("assistant", "");
        var changed = false;
        bubble.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatBubble.Content))
                changed = true;
        };

        bubble.AppendContent("Hello ");
        bubble.AppendContent("World");

        bubble.Content.Should().Be("Hello World");
        changed.Should().BeTrue();
    }

    [Fact]
    public void ChatBubble_SetContent_ShouldReplaceContent()
    {
        var bubble = new ChatBubble("assistant", "old");

        bubble.SetContent("new content");

        bubble.Content.Should().Be("new content");
    }

    [Fact]
    public void ChatBubble_Timestamp_ShouldBeRecent()
    {
        var before = DateTimeOffset.Now.AddSeconds(-1);
        var bubble = new ChatBubble("user", "test");
        var after = DateTimeOffset.Now.AddSeconds(1);

        bubble.Timestamp.Should().BeAfter(before);
        bubble.Timestamp.Should().BeBefore(after);
    }

    // --- Helper ---

    private static LlmChatToolViewModel CreateViewModel()
    {
        var workspaceService = Substitute.For<NVS.Core.Interfaces.IWorkspaceService>();
        var editorService = Substitute.For<NVS.Core.Interfaces.IEditorService>();
        var fileSystemService = Substitute.For<NVS.Core.Interfaces.IFileSystemService>();
        var gitService = Substitute.For<NVS.Core.Interfaces.IGitService>();
        var terminalService = Substitute.For<NVS.Core.Interfaces.ITerminalService>();
        var settingsService = Substitute.For<NVS.Core.Interfaces.ISettingsService>();
        settingsService.AppSettings.Returns(new NVS.Core.Models.Settings.AppSettings());
        var solutionService = Substitute.For<NVS.Core.Interfaces.ISolutionService>();
        var buildService = Substitute.For<NVS.Core.Interfaces.IBuildService>();
        var editorVm = new NVS.ViewModels.EditorViewModel(editorService, fileSystemService, null, null);

        var mainVm = new NVS.ViewModels.MainViewModel(
            workspaceService, editorService, fileSystemService,
            editorVm, gitService, terminalService, settingsService,
            solutionService, buildService);

        return new LlmChatToolViewModel(mainVm);
    }
}
