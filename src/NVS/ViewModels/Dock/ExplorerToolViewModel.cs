using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public class ExplorerToolViewModel : Tool
{
    public MainViewModel Main { get; }

    public ExplorerToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Explorer";
        Title = "📁 Explorer";
        CanClose = false;
        CanPin = true;
    }
}
