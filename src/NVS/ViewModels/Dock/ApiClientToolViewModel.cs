using ApiClient.UI.ViewModels;
using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public class ApiClientToolViewModel : Tool
{
    public MainViewModel Main { get; }
    public WorkspaceViewModel ApiClientViewModel { get; }

    public ApiClientToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "ApiClient";
        Title = "🌐 API Client";
        CanClose = true;
        CanPin = true;

        ApiClientViewModel = new WorkspaceViewModel();
    }
}
