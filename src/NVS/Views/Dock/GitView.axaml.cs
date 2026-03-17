using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NVS.Core.Interfaces;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class GitView : UserControl
{
    public GitView()
    {
        InitializeComponent();
    }

    private MainViewModel? GetMain() =>
        (DataContext as GitToolViewModel)?.Main;

    private void OnStagedFileClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock { DataContext: GitFileStatus file })
        {
            var main = GetMain();
            main?.GitViewDiffCommand.Execute(file);
        }
    }

    private void OnChangedFileClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock { DataContext: GitFileStatus file })
        {
            var main = GetMain();
            main?.GitViewDiffCommand.Execute(file);
        }
    }

    private async void OnCreateBranchClick(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var result = await DialogHelper.PromptNewBranchAsync(window);
        if (result is null || string.IsNullOrWhiteSpace(result.Value.Name)) return;

        var main = GetMain();
        if (main is not null)
            await main.GitCreateBranchAsync(result.Value.Name, result.Value.IncludeChanges);
    }

    private async void OnDeleteBranchClick(object? sender, RoutedEventArgs e)
    {
        var main = GetMain();
        var branch = main?.SelectedGitBranch;
        if (main is null || branch is null || branch.IsCurrent) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var confirmed = await DialogHelper.ConfirmDeleteAsync(window, $"branch '{branch.Name}'");
        if (confirmed)
            await main.GitDeleteBranchAsync(branch.Name);
    }
}
