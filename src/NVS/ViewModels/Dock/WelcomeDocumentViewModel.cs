using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public sealed partial class WelcomeDocumentViewModel : Document
{
    public MainViewModel Main { get; }

    public WelcomeDocumentViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Welcome";
        Title = "Welcome";
        CanClose = true;
        CanPin = false;

        Main.SettingsService.AppSettingsChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RecentWorkspaces));
            OnPropertyChanged(nameof(HasRecents));
        };
    }

    /// <summary>Recently opened workspaces, most recent first.</summary>
    public IReadOnlyList<string> RecentWorkspaces => Main.SettingsService.AppSettings.RecentWorkspaces;

    /// <summary>Whether the recents section should be shown.</summary>
    public bool HasRecents => RecentWorkspaces.Count > 0;

    [RelayCommand]
    private async Task OpenRecent(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            await Main.OpenWorkspaceAsync(path);
        }
    }
}
