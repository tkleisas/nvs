using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public class EditorDocumentViewModel : Document
{
    public MainViewModel Main { get; }

    public EditorDocumentViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Editor";
        Title = "Editor";
        CanClose = false;
    }
}
