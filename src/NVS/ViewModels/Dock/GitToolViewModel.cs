using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public class GitToolViewModel : Tool
{
    public MainViewModel Main { get; }

    public GitToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Git";
        Title = "🔀 Source Control";
        CanClose = false;
        CanPin = true;
    }
}
