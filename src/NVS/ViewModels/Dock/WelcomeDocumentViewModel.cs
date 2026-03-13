using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public sealed class WelcomeDocumentViewModel : Document
{
    public MainViewModel Main { get; }

    public WelcomeDocumentViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Welcome";
        Title = "Welcome";
        CanClose = true;
        CanPin = false;
    }
}
