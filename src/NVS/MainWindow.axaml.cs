using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NVS.ViewModels;
using NVS.Views;
using NVS.Views.Dock;

namespace NVS;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var dirtyDocs = vm.Editor?.OpenDocuments.Where(d => d.IsDirty).ToList();
        if (dirtyDocs is null || dirtyDocs.Count == 0) return;

        e.Cancel = true;

        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 400, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#2D2D30"),
            CanResize = false,
        };

        var names = string.Join(", ", dirtyDocs.Select(d => d.Document.Name));
        var result = false;

        var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Save changes to {dirtyDocs.Count} file(s)?\n{names}",
            Foreground = Avalonia.Media.Brush.Parse("#CCCCCC"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var saveBtn = new Button { Content = "Save All", Background = Avalonia.Media.Brush.Parse("#007ACC"), Foreground = Avalonia.Media.Brush.Parse("White"), Padding = new Avalonia.Thickness(16, 6) };
        var discardBtn = new Button { Content = "Don't Save", Padding = new Avalonia.Thickness(16, 6) };
        var cancelBtn = new Button { Content = "Cancel", Padding = new Avalonia.Thickness(16, 6) };

        saveBtn.Click += async (_, _) => { await vm.SaveAllCommand.ExecuteAsync(null); result = true; dialog.Close(); };
        discardBtn.Click += (_, _) => { result = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => { result = false; dialog.Close(); };

        buttons.Children.Add(saveBtn);
        buttons.Children.Add(discardBtn);
        buttons.Children.Add(cancelBtn);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(this);

        if (result)
        {
            Closing -= OnWindowClosing;
            Close();
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnGoToLineClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.Editor?.ActiveDocument is null) return;

        var dialog = new Window
        {
            Title = "Go to Line",
            Width = 300, Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#2D2D30"),
            CanResize = false,
        };

        var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 8 };
        var input = new TextBox
        {
            Watermark = "Line number",
            Background = Avalonia.Media.Brush.Parse("#3C3C3C"),
            Foreground = Avalonia.Media.Brush.Parse("#CCCCCC"),
        };
        var okBtn = new Button
        {
            Content = "Go",
            Background = Avalonia.Media.Brush.Parse("#007ACC"),
            Foreground = Avalonia.Media.Brush.Parse("White"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(20, 6),
        };

        okBtn.Click += (_, _) =>
        {
            if (int.TryParse(input.Text, out var line) && line > 0)
            {
                vm.Editor.ActiveDocument.CursorLine = line;
                vm.StatusMessage = $"Jumped to line {line}";
            }
            dialog.Close();
        };

        input.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter)
            {
                okBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ke.Handled = true;
            }
        };

        panel.Children.Add(input);
        panel.Children.Add(okBtn);
        dialog.Content = panel;

        await dialog.ShowDialog(this);
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        await about.ShowDialog(this);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var app = App.Current;
        if (app?.Services is null) return;

        var settingsService = app.Services.GetService(typeof(NVS.Core.Interfaces.ISettingsService)) as NVS.Core.Interfaces.ISettingsService;
        var serverManager = app.Services.GetService(typeof(NVS.Core.Interfaces.ILanguageServerManager)) as NVS.Core.Interfaces.ILanguageServerManager;
        if (settingsService is null || serverManager is null) return;

        var vm = new ViewModels.SettingsViewModel(settingsService, serverManager);
        await vm.InitializeAsync();

        var window = new SettingsWindow { DataContext = vm };
        await window.ShowDialog(this);
    }
}
