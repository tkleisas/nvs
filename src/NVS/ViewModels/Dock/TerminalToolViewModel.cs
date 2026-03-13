using System.ComponentModel;
using System.Runtime.CompilerServices;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.ViewModels.Dock;

public class TerminalToolViewModel : Tool
{
    private string _terminalFontFamily;
    private int _terminalFontSize;
    private int _terminalBufferSize;

    public MainViewModel Main { get; }
    public string ShellPath { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";

    private readonly Queue<string> _pendingCommands = new();

    /// <summary>
    /// Callback set by the view to send a command to the PTY terminal.
    /// </summary>
    public Func<string, Task>? SendCommandAsync { get; set; }

    /// <summary>
    /// Enqueues a command to be sent to the terminal. If the PTY is ready,
    /// sends immediately; otherwise queues for delivery when ready.
    /// </summary>
    public async Task SendCommandToTerminalAsync(string command)
    {
        if (SendCommandAsync is not null)
        {
            await SendCommandAsync(command);
        }
        else
        {
            _pendingCommands.Enqueue(command);
        }
    }

    /// <summary>
    /// Called by the view when the PTY is ready to accept input.
    /// Flushes any pending commands.
    /// </summary>
    public async Task FlushPendingCommandsAsync()
    {
        if (SendCommandAsync is null) return;
        while (_pendingCommands.TryDequeue(out var command))
        {
            await SendCommandAsync(command);
        }
    }

    public string TerminalFontFamily
    {
        get => _terminalFontFamily;
        private set { if (_terminalFontFamily != value) { _terminalFontFamily = value; OnPropertyChanged(); } }
    }

    public int TerminalFontSize
    {
        get => _terminalFontSize;
        private set { if (_terminalFontSize != value) { _terminalFontSize = value; OnPropertyChanged(); } }
    }

    public int TerminalBufferSize
    {
        get => _terminalBufferSize;
        private set { if (_terminalBufferSize != value) { _terminalBufferSize = value; OnPropertyChanged(); } }
    }

    public TerminalToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "Terminal";
        Title = "⌨ Terminal";
        CanClose = true;
        CanPin = true;

        ShellPath = GetDefaultShell();

        var settings = main.SettingsService.AppSettings.Terminal;
        _terminalFontFamily = settings.FontFamily;
        _terminalFontSize = settings.FontSize;
        _terminalBufferSize = settings.BufferSize;

        main.SettingsService.AppSettingsChanged += OnAppSettingsChanged;
    }

    private void OnAppSettingsChanged(object? sender, AppSettings settings)
    {
        TerminalFontFamily = settings.Terminal.FontFamily;
        TerminalFontSize = settings.Terminal.FontSize;
        TerminalBufferSize = settings.Terminal.BufferSize;
    }

    private static string GetDefaultShell()
    {
        if (OperatingSystem.IsWindows())
            return "powershell.exe";
        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
    }

    private new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
    }
}
