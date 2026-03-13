using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Model.Controls;
using Dock.Model.Core;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.Views;
using NVS.Views.Dock;

namespace NVS;

public partial class MainWindow : Window
{
    private double _restoreWidth = 1200;
    private double _restoreHeight = 800;
    private double? _restoreX;
    private double? _restoreY;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
        Opened += OnWindowOpened;
    }

    public void ApplyWindowSettings(WindowSettings settings)
    {
        _restoreWidth = settings.Width > 0 ? settings.Width : 1200;
        _restoreHeight = settings.Height > 0 ? settings.Height : 800;

        Width = _restoreWidth;
        Height = _restoreHeight;

        if (settings.X.HasValue && settings.Y.HasValue)
        {
            _restoreX = settings.X;
            _restoreY = settings.Y;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)settings.X.Value, (int)settings.Y.Value);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (settings.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        // Record initial normal-state bounds
        if (WindowState == WindowState.Normal)
        {
            RecordNormalBounds();
        }

        // Track changes so we always have the last normal-state bounds
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty ||
                args.Property == BoundsProperty)
            {
                RecordNormalBounds();
            }
        };

        PositionChanged += (_, _) => RecordNormalBounds();
    }

    private void RecordNormalBounds()
    {
        if (WindowState != WindowState.Normal) return;
        if (ClientSize.Width > 0) _restoreWidth = ClientSize.Width;
        if (ClientSize.Height > 0) _restoreHeight = ClientSize.Height;
        _restoreX = Position.X;
        _restoreY = Position.Y;
    }

    private void SaveWindowState()
    {
        var app = App.Current;
        if (app?.Services is null) return;

        var settingsService = app.Services.GetService(typeof(ISettingsService)) as ISettingsService;
        if (settingsService is null) return;

        var windowSettings = new WindowSettings
        {
            IsMaximized = WindowState == WindowState.Maximized,
            Width = _restoreWidth,
            Height = _restoreHeight,
            X = _restoreX,
            Y = _restoreY,
        };

        var dockSettings = ExtractDockProportions();

        var newSettings = settingsService.AppSettings with
        {
            Window = windowSettings,
            Dock = dockSettings,
        };
        try
        {
            Task.Run(() => settingsService.SaveAppSettingsAsync(newSettings)).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort: don't prevent window close if save fails
        }
    }

    private DockLayoutSettings ExtractDockProportions()
    {
        if (DataContext is MainViewModel vm && vm.DockLayout is IRootDock root)
        {
            try
            {
                // Navigate: RootDock → HomeViewModel → mainLayout (ProportionalDock)
                if (root.ActiveDockable is IDock homeDock
                    && homeDock.VisibleDockables?.Count > 0
                    && homeDock.VisibleDockables[0] is IProportionalDock mainLayout
                    && mainLayout.VisibleDockables?.Count >= 3)
                {
                    double leftProp = 0.22;
                    double bottomProp = 0.25;

                    if (mainLayout.VisibleDockables[0] is IProportionalDock leftDock)
                    {
                        leftProp = leftDock.Proportion;
                    }

                    if (mainLayout.VisibleDockables[2] is IProportionalDock centerWithBottom
                        && centerWithBottom.VisibleDockables?.Count >= 3
                        && centerWithBottom.VisibleDockables[2] is IProportionalDock bottomDock)
                    {
                        bottomProp = bottomDock.Proportion;
                    }

                    return new DockLayoutSettings
                    {
                        LeftPanelProportion = leftProp,
                        BottomPanelProportion = bottomProp,
                    };
                }
            }
            catch
            {
                // Fall through to defaults
            }
        }

        return new DockLayoutSettings();
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        SaveWindowState();

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

    private async void OnNewProjectClick(object? sender, RoutedEventArgs e)
    {
        var app = App.Current;
        var templateService = app?.Services?.GetService(typeof(ITemplateService)) as ITemplateService;
        if (templateService is null) return;

        var vm = new NewProjectViewModel(templateService);
        var window = new NewProjectWindow { DataContext = vm };
        var result = await window.ShowDialog<bool?>(this);

        if (result == true && vm.CreatedProjectPath is not null && DataContext is MainViewModel mainVm)
        {
            await mainVm.OpenWorkspaceAsync(vm.CreatedProjectPath);
        }
    }

    private async void OnNewFileFromTemplateClick(object? sender, RoutedEventArgs e)
    {
        var app = App.Current;
        var templateService = app?.Services?.GetService(typeof(ITemplateService)) as ITemplateService;
        if (templateService is null) return;

        var mainVm = DataContext as MainViewModel;
        var currentDir = mainVm?.WorkspacePath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string? projectRoot = null;
        if (mainVm?.WorkspacePath is not null)
            projectRoot = mainVm.WorkspacePath;

        var vm = new NewFileViewModel(templateService, currentDir, projectRoot);
        var window = new NewFileWindow { DataContext = vm };
        var result = await window.ShowDialog<bool?>(this);

        if (result == true && vm.CreatedFilePath is not null && mainVm is not null)
        {
            await mainVm.OpenFileAsync(vm.CreatedFilePath);
        }
    }

    private async void OnAddProjectToSolutionClick(object? sender, RoutedEventArgs e)
    {
        var mainVm = DataContext as MainViewModel;
        if (mainVm is null) return;

        var app = App.Current;
        var solutionService = app?.Services?.GetService(typeof(ISolutionService)) as ISolutionService;
        var templateService = app?.Services?.GetService(typeof(ITemplateService)) as ITemplateService;
        if (solutionService is null || templateService is null) return;

        // Must have a solution loaded
        if (solutionService.CurrentSolution is null)
        {
            mainVm.StatusMessage = "No solution loaded — open or create a project first";
            return;
        }

        var vm = new NewProjectViewModel(templateService) { CreateSolution = false };
        vm.Location = Path.GetDirectoryName(solutionService.CurrentSolution.FilePath)
                      ?? mainVm.WorkspacePath
                      ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var window = new NewProjectWindow { DataContext = vm, Title = "Add New Project to Solution" };
        var result = await window.ShowDialog<bool?>(this);

        if (result == true && vm.CreatedProjectPath is not null)
        {
            try
            {
                // Find the .csproj in the created project
                var csprojFiles = Directory.GetFiles(vm.CreatedProjectPath, "*.csproj", SearchOption.TopDirectoryOnly);
                if (csprojFiles.Length > 0)
                {
                    await solutionService.AddProjectToSolutionAsync(
                        solutionService.CurrentSolution.FilePath, csprojFiles[0]);

                    // Reload the solution to pick up the new project
                    await solutionService.LoadSolutionAsync(solutionService.CurrentSolution.FilePath);
                    mainVm.StatusMessage = $"Project '{vm.ProjectName}' added to solution";
                }

                // Refresh file tree
                if (mainVm.WorkspacePath is not null)
                    await mainVm.OpenWorkspaceAsync(mainVm.WorkspacePath);
            }
            catch (Exception ex)
            {
                mainVm.StatusMessage = $"Failed to add project: {ex.Message}";
            }
        }
    }
}
