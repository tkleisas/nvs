using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NVS.ViewModels;
using NVS.Views;

namespace NVS;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnFileTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TreeView treeView)
        {
            var selectedItem = treeView.SelectedItem as FileTreeNode;
            if (selectedItem != null && !selectedItem.IsDirectory)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.OpenFileFromTreeCommand.Execute(selectedItem);
                }
            }
        }
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        await about.ShowDialog(this);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var app = App.Current;
        if (app?.Services is null) return;

        var settingsService = app.Services.GetService(typeof(NVS.Core.Interfaces.ISettingsService)) as NVS.Core.Interfaces.ISettingsService;
        var serverManager = app.Services.GetService(typeof(NVS.Core.Interfaces.ILanguageServerManager)) as NVS.Core.Interfaces.ILanguageServerManager;
        if (settingsService is null || serverManager is null) return;

        var vm = new ViewModels.SettingsViewModel(settingsService, serverManager);
        await vm.InitializeAsync();

        var window = new SettingsWindow { DataContext = vm };
        await window.ShowDialog(this);
    }
}
