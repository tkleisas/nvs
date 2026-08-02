using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using NVS.ViewModels;

namespace NVS.Views;

/// <summary>
/// "Unsaved changes" confirmation shown when closing a dirty document tab:
/// Save / Don't Save / Cancel (Escape cancels).
/// </summary>
public static class UnsavedChangesDialog
{
    public static Task<DirtyCloseChoice> ShowAsync(Window owner, string documentName)
    {
        var completion = new TaskCompletionSource<DirtyCloseChoice>();

        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var message = new TextBlock
        {
            Text = $"Save changes to \"{documentName}\" before closing?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 16),
        };

        var saveButton = new Button { Content = "Save", Padding = new Avalonia.Thickness(16, 6) };
        var discardButton = new Button { Content = "Don't Save", Padding = new Avalonia.Thickness(16, 6), Margin = new Avalonia.Thickness(8, 0, 0, 0) };
        var cancelButton = new Button { Content = "Cancel", Padding = new Avalonia.Thickness(16, 6), Margin = new Avalonia.Thickness(8, 0, 0, 0) };

        saveButton.Click += (_, _) => { completion.TrySetResult(DirtyCloseChoice.Save); dialog.Close(); };
        discardButton.Click += (_, _) => { completion.TrySetResult(DirtyCloseChoice.Discard); dialog.Close(); };
        cancelButton.Click += (_, _) => { completion.TrySetResult(DirtyCloseChoice.Cancel); dialog.Close(); };
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                completion.TrySetResult(DirtyCloseChoice.Cancel);
                dialog.Close();
            }
        };
        dialog.Closed += (_, _) => completion.TrySetResult(DirtyCloseChoice.Cancel);

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children =
            {
                message,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { saveButton, discardButton, cancelButton }
                }
            }
        };

        _ = dialog.ShowDialog(owner);
        return completion.Task;
    }
}
