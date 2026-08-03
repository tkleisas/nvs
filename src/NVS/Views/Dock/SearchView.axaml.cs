using Avalonia.Controls;
using Avalonia.Input;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SearchToolViewModel tool)
        {
            tool.Main.Search.ConfirmReplaceAll = ConfirmReplaceAllAsync;
        }
    }

    private async Task<bool> ConfirmReplaceAllAsync(int matchCount, int fileCount)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return false;
        }

        return await DialogHelper.ConfirmAsync(
            owner,
            "Replace in Files",
            $"Replace {matchCount} occurrence(s) in {fileCount} file(s)? This modifies the files on disk.",
            "Replace All");
    }

    private void OnSearchQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SearchToolViewModel tool)
        {
            tool.Main.Search.SearchFilesCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is FileSearchResult result
            && DataContext is SearchToolViewModel tool)
        {
            await tool.Main.Search.OpenSearchResultCommand.ExecuteAsync(result);
        }
    }
}
