using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class TerminalView : UserControl
{
    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyFontSettings();

        if (DataContext is TerminalToolViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(TerminalToolViewModel.TerminalFontFamily)
                    or nameof(TerminalToolViewModel.TerminalFontSize))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(ApplyFontSettings);
                }
            };
        }
    }

    private void ApplyFontSettings()
    {
        if (Terminal == null || DataContext is not TerminalToolViewModel vm) return;

        if (!string.IsNullOrWhiteSpace(vm.TerminalFontFamily))
            Terminal.FontFamily = new FontFamily(vm.TerminalFontFamily);
        if (vm.TerminalFontSize > 0)
            Terminal.FontSize = vm.TerminalFontSize;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TerminalToolViewModel vm && Terminal != null)
        {
            Terminal.Process = vm.ShellPath;
            var workDir = vm.WorkingDirectory;
            if (string.IsNullOrEmpty(workDir))
                workDir = vm.Main.WorkspacePath;

            if (!string.IsNullOrEmpty(workDir))
            {
                if (OperatingSystem.IsWindows())
                    Terminal.Args = ["-NoExit", "-Command", $"Set-Location '{workDir}'"];
                else
                    Terminal.Args = ["-c", $"cd \"{workDir}\" && exec $SHELL"];
            }

            // Wire up the SendCommandAsync delegate so RunProject can send
            // commands directly to the Iciclecreek PTY terminal.
            vm.SendCommandAsync = async command =>
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        // Find the inner Iciclecreek.Terminal.TerminalView inside the
                        // TerminalControl template, then invoke the private SendToPtyAsync
                        // method to write directly to the PTY stream.
                        var innerView = Terminal?.GetVisualDescendants()
                            .OfType<Iciclecreek.Terminal.TerminalView>()
                            .FirstOrDefault();

                        if (innerView is not null)
                        {
                            var method = innerView.GetType().GetMethod(
                                "SendToPtyAsync",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                            if (method is not null)
                            {
                                var task = method.Invoke(innerView,
                                    [command + "\r", System.Threading.CancellationToken.None]) as Task;
                                if (task is not null)
                                    await task;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Terminal may not be ready yet
                    }
                });
            };

            ApplyFontSettings();
        }
    }
}
