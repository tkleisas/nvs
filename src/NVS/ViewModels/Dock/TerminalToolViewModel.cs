using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public class TerminalToolViewModel : Tool
{
    public MainViewModel Main { get; }

    public TerminalToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Terminal";
        Title = "⌨ Terminal";
        CanClose = true;
        CanPin = true;
    }
}
