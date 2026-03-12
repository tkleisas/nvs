using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class TerminalView : UserControl
{
    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TerminalToolViewModel tool)
        {
            tool.Main.PropertyChanged += OnMainPropertyChanged;
        }
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "TerminalOutput")
        {
            TerminalScrollViewer.ScrollToEnd();
        }
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
