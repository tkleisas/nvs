using Avalonia;
using Avalonia.Controls;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class TerminalView : UserControl
{
    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        }
    }
}
