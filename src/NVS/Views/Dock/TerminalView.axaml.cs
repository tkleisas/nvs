using Avalonia.Controls;
using Avalonia.Input;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class TerminalView : UserControl
{
    public TerminalView()
    {
        InitializeComponent();
    }

    private void OnTerminalInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TerminalToolViewModel tool)
        {
            tool.Main.SendTerminalInputCommand.Execute(null);
            e.Handled = true;
        }
    }
}
