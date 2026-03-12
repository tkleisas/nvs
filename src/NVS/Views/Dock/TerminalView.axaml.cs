using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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

            ApplyFontSettings();
        }
    }
}
