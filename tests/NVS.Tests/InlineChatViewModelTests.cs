using NVS.Core.Interfaces;
using NVS.Core.LLM;
using NVS.Core.Models.Settings;
using NVS.ViewModels;

namespace NVS.Tests;

public class InlineChatViewModelTests
{
    private sealed class FakeLlmService : ILlmService
    {
        public bool IsConfigured => true;
        public string Reply { get; set; } = "x = 2;";

        public bool IsProcessing => false;
        public IReadOnlyList<LlmModelConfig> GetAvailableModels() => [];
        public event EventHandler? RequestStarted { add { } remove { } }
        public event EventHandler? RequestCompleted { add { } remove { } }
        public event EventHandler<LlmErrorEventArgs>? ErrorOccurred { add { } remove { } }

        public Task<LlmResponse> SendAsync(ChatCompletionRequest request, Action<string>? onToken = null, CancellationToken cancellationToken = default, string? modelId = null, Action<string>? onReasoningToken = null)
            => Task.FromResult(new LlmResponse { Content = Reply, InputTokens = 1, OutputTokens = 1, Model = "m" });
        public Task<AgentLoopResult> RunAgentLoopAsync(List<ChatCompletionMessage> messages, IReadOnlyList<ToolDefinition>? tools = null, string? systemPrompt = null, Action<string>? onToken = null, Action<AgentToolCallEvent>? onToolCall = null, Func<ToolApprovalRequest, Task<bool>>? onApprovalRequired = null, int maxIterations = 20, CancellationToken cancellationToken = default, string? modelId = null, Action<string>? onReasoningToken = null)
            => Task.FromResult(new AgentLoopResult { Content = Reply, Iterations = 1, TotalInputTokens = 1, TotalOutputTokens = 1 });
        public void CancelCurrentRequest() { }
    }

    /// <summary>Test double for the editor selection (no live TextEditor in tests).</summary>
    private sealed class FakeSelection : Behaviors.IEditorSelection
    {
        public string Text = "x = 1;\ny = 3;";
        public bool HasSelection { get; set; }
        public string SelectedText { get; set; } = string.Empty;
        public string AllText => Text;

        public void ReplaceSelectionOrInsertAtCaret(string text)
        {
            if (HasSelection)
            {
                Text = Text.Replace(SelectedText, text);
            }
            else
            {
                Text = text + "\n" + Text;
            }
        }
    }

    private static (MainViewModel Main, InlineChatViewModel Vm, FakeSelection Selection) CreateVm()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());
        var main = new MainViewModel(
            Substitute.For<IWorkspaceService>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IFileSystemService>(),
            new EditorViewModel(Substitute.For<IEditorService>(), Substitute.For<IFileSystemService>()),
            Substitute.For<IGitService>(),
            Substitute.For<ITerminalService>(),
            settings,
            Substitute.For<ISolutionService>(),
            Substitute.For<IBuildService>());

        main.Editor!.NewFile();
        var selection = new FakeSelection();
        main.Editor.ActiveDocument!.Selection = selection;

        var vm = new InlineChatViewModel(main) { LlmServiceProvider = () => new FakeLlmService() };
        return (main, vm, selection);
    }

    [Fact]
    public void Open_WithDocument_SetsContextSummary()
    {
        var (_, vm, _) = CreateVm();

        vm.Open();

        vm.IsOpen.Should().BeTrue();
        vm.ContextSummary.Should().Contain("caret");
    }

    [Fact]
    public async Task SubmitThenApply_InsertsProposal()
    {
        var (_, vm, selection) = CreateVm();
        vm.Open();
        vm.Instruction = "change it";

        await vm.SubmitCommand.ExecuteAsync(null);

        vm.HasPreview.Should().BeTrue();
        vm.PreviewRows.Should().Contain(r => r.Text == "x = 2;");

        vm.ApplyCommand.Execute(null);

        selection.Text.Should().Contain("x = 2;");
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Submit_WithSelection_ReplacesSelection()
    {
        var (_, vm, selection) = CreateVm();
        selection.HasSelection = true;
        selection.SelectedText = "x = 1;";

        vm.Open();
        vm.ContextSummary.Should().Contain("selection");

        vm.Instruction = "change it";
        await vm.SubmitCommand.ExecuteAsync(null);
        vm.ApplyCommand.Execute(null);

        selection.Text.Should().Be("x = 2;\ny = 3;");
    }
}
