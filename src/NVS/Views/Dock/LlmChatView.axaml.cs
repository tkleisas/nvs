using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class LlmChatView : UserControl
{
    public LlmChatView()
    {
        InitializeComponent();

        // Tunneling so we see Enter before the TextBox turns it into a newline
        var input = this.FindControl<TextBox>("InputBox");
        input?.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        // Enter and Ctrl+Enter send; Shift+Enter inserts a newline
        var sends = e.KeyModifiers == KeyModifiers.None
                    || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!sends)
            return;

        if (DataContext is LlmChatToolViewModel vm && vm.SendMessageCommand.CanExecute(null))
        {
            vm.SendMessageCommand.Execute(null);
            e.Handled = true;
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
