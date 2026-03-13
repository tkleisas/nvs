using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NVS.ViewModels;

namespace NVS.Views;

public partial class NewProjectWindow : Window
{
    public NewProjectWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is NewProjectViewModel vm)
        {
            vm.RequestClose += (_, _) => Close(true);
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Project Location",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && DataContext is NewProjectViewModel vm)
        {
            vm.Location = folders[0].Path.LocalPath;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
