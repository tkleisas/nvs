using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public class TerminalToolViewModel : Tool
{
    public MainViewModel Main { get; }
    public string ShellPath { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";

    public TerminalToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Terminal";
        Title = "⌨ Terminal";
        CanClose = true;
        CanPin = true;

        ShellPath = GetDefaultShell();
    }

    private static string GetDefaultShell()
    {
        if (OperatingSystem.IsWindows())
            return "powershell.exe";
        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
    }
}
