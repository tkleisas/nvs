using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public class SearchToolViewModel : Tool
{
    public MainViewModel Main { get; }

    public SearchToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Search";
        Title = "🔍 Search";
        CanClose = false;
        CanPin = true;
    }
}
