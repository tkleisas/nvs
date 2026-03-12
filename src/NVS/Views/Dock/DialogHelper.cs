using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NVS.Views.Dock;

internal static class DialogHelper
{
    public static async Task<string?> PromptForNameAsync(Window owner, string title, string label, string defaultValue = "")
    {
        var dialog = new Window
        {
            Title = title,
            Width = 350, Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#2D2D30"),
            CanResize = false,
        };

        string? result = null;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 8 };
        var input = new TextBox
        {
            Text = defaultValue,
            Watermark = label,
            Background = Avalonia.Media.Brush.Parse("#3C3C3C"),
            Foreground = Avalonia.Media.Brush.Parse("#CCCCCC"),
        };
        var okBtn = new Button
        {
            Content = "OK",
            Background = Avalonia.Media.Brush.Parse("#007ACC"),
            Foreground = Avalonia.Media.Brush.Parse("White"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(20, 6),
        };

        okBtn.Click += (_, _) => { result = input.Text; dialog.Close(); };
        input.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) { result = input.Text; dialog.Close(); ke.Handled = true; }
            if (ke.Key == Key.Escape) { dialog.Close(); ke.Handled = true; }
        };

        panel.Children.Add(input);
        panel.Children.Add(okBtn);
        dialog.Content = panel;

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<bool> ConfirmDeleteAsync(Window owner, string name)
    {
        var dialog = new Window
        {
            Title = "Confirm Delete",
            Width = 380, Height = 130,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#2D2D30"),
            CanResize = false,
        };

        var confirmed = false;
        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Delete \"{name}\"?",
            Foreground = Avalonia.Media.Brush.Parse("#CCCCCC"),
        });

        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var delBtn = new Button { Content = "Delete", Background = Avalonia.Media.Brush.Parse("#F44747"), Foreground = Avalonia.Media.Brush.Parse("White"), Padding = new Avalonia.Thickness(16, 6) };
        var cancelBtn = new Button { Content = "Cancel", Padding = new Avalonia.Thickness(16, 6) };
        delBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => { dialog.Close(); };
        buttons.Children.Add(delBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(owner);
        return confirmed;
    }
}
