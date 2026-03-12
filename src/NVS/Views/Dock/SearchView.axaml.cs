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
    }

    private void OnSearchQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SearchToolViewModel tool)
        {
            tool.Main.SearchFilesCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is FileSearchResult result
            && DataContext is SearchToolViewModel tool)
        {
            await tool.Main.OpenSearchResultCommand.ExecuteAsync(result);
        }
    }
}
