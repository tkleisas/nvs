using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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

        KeyDown += OnKeyDown;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private async void OnCopyInfoClick(object? sender, RoutedEventArgs e)
    {
        var text = $"NVS {AppVersionInfo.InformationalVersion}\n" +
                   $"Commit: {AppVersionInfo.GitHash}\n" +
                   $"Runtime: {AppVersionInfo.RuntimeVersion}\n" +
                   $"OS: {AppVersionInfo.OsDescription} ({AppVersionInfo.Architecture})";

        if (Clipboard is not null)
        {
            await Clipboard.SetTextAsync(text);
        }
    }
}
