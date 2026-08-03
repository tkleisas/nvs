using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using NVS.Core.Models;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class ContainersView : UserControl
{
    public ContainersView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ContainersToolViewModel vm)
        {
            vm.ProjectPickRequested += OnProjectPickRequested;
            vm.LogsRequested += OnLogsRequested;
        }
    }

    private async void OnProjectPickRequested(object? sender, ProjectPickRequest request)
    {
        if (DataContext is not ContainersToolViewModel vm)
        {
            return;
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
        {
            return;
        }

        if (request.ForScaffold)
        {
            var projects = vm.Main.SolutionService.GetLoadedProjects();
            if (projects.Count == 1)
            {
                request.ScaffoldProject = projects[0];
                return;
            }

            request.ScaffoldProject = await ShowProjectPickerAsync(owner, projects);
        }
        else
        {
            // Pick a Dockerfile via file dialog, then a tag
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Dockerfile",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Dockerfile") { Patterns = ["Dockerfile", "*.dockerfile"] },
                    new FilePickerFileType("All Files") { Patterns = ["*"] }
                ]
            });

            if (files.Count == 0) return;
            request.DockerfilePath = files[0].Path.LocalPath;

            var tag = await DialogHelper.PromptForNameAsync(owner, "Image Tag", "Image:tag (e.g. myapp:latest)");
            if (string.IsNullOrWhiteSpace(tag)) return;
            request.ImageTag = tag.Trim();
        }
    }

    private void OnLogsRequested(object? sender, string logs)
    {
        var window = new Window
        {
            Title = "Container Logs",
            Width = 720,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer
            {
                Content = new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(logs) ? "(no logs)" : logs,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.NoWrap,
                    FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                    FontSize = 12,
                    Margin = new Avalonia.Thickness(8),
                    BorderThickness = new Avalonia.Thickness(0),
                    Background = Avalonia.Media.Brushes.Transparent,
                }
            }
        };

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    private static async Task<ProjectModel?> ShowProjectPickerAsync(Window owner, IReadOnlyList<ProjectModel> projects)
    {
        var completion = new TaskCompletionSource<ProjectModel?>();

        var combo = new ComboBox
        {
            ItemsSource = projects,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        combo.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProjectModel>((p, _) =>
            new TextBlock { Text = p.Name });

        Window? dialog = null;
        Button MakeButton(string text, Action onClick)
        {
            var button = new Button { Content = text, Padding = new Avalonia.Thickness(14, 6) };
            button.Click += (_, _) => { onClick(); dialog!.Close(); };
            return button;
        }

        dialog = new Window
        {
            Title = "Select Project",
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Generate Dockerfile for project:" },
                    combo,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            MakeButton("Generate", () => { completion.TrySetResult((ProjectModel)combo.SelectedItem!); }),
                            MakeButton("Cancel", () => completion.TrySetResult(null)),
                        }
                    }
                }
            }
        };

        dialog.Closed += (_, _) => completion.TrySetResult(null);
        await dialog.ShowDialog(owner);
        return await completion.Task;
    }
}
