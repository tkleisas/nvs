using Avalonia.Controls;
using Avalonia.Interactivity;
using NVS.ViewModels;

namespace NVS.Views;

public partial class NewFileWindow : Window
{
    public NewFileWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is NewFileViewModel vm)
        {
            vm.RequestClose += (_, _) => Close(true);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
