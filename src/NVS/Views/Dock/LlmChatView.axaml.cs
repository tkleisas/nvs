using Avalonia.Controls;
using Avalonia.Input;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class LlmChatView : UserControl
{
    public LlmChatView()
    {
        InitializeComponent();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            if (DataContext is LlmChatToolViewModel vm && vm.SendMessageCommand.CanExecute(null))
            {
                vm.SendMessageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is LlmChatToolViewModel vm)
        {
            vm.Messages.CollectionChanged += (_, _) => ScrollToBottom();
        }
    }

    private void ScrollToBottom()
    {
        var scroller = this.FindControl<ScrollViewer>("MessageScroller");
        scroller?.ScrollToEnd();
    }
}
