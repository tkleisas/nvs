using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NVS.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        VersionText.Text = AppVersionInfo.Version;
        CommitText.Text = AppVersionInfo.GitHash;
        RuntimeText.Text = AppVersionInfo.RuntimeVersion;
        OsText.Text = AppVersionInfo.OsDescription;
        ArchText.Text = AppVersionInfo.Architecture;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
