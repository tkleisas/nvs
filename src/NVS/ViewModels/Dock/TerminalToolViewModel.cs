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

    /// <summary>The new Porta.Pty-backed terminal session, created on-demand via ITerminalHost.</summary>
    public IProcessTerminal? Terminal
    {
        get => _terminal;
        set
        {
            _terminal = value;
            if (value is not null)
            {
                // Commands enqueued before the PTY existed can go out now.
                _ = FlushPendingCommandsAsync();
            }
        }
    }

    private IProcessTerminal? _terminal;
    private readonly Queue<string> _pendingCommands = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);

    /// <summary>
    /// Enqueues a command to be sent to the terminal. Goes straight to the PTY
    /// when the session is running; otherwise queues until it appears.
    /// </summary>
    public async Task SendCommandToTerminalAsync(string command)
    {
        if (Terminal is not null && Terminal.IsRunning)
        {
            await Terminal.SendInputAsync(command + "\r");
            return;
        }

        _pendingCommands.Enqueue(command);
    }

    /// <summary>
    /// Flushes commands enqueued before the terminal session existed.
    /// </summary>
    public async Task FlushPendingCommandsAsync()
    {
        await _flushLock.WaitAsync();
        try
        {
            while (_pendingCommands.TryDequeue(out var command))
            {
                if (Terminal is not null && Terminal.IsRunning)
                {
                    await Terminal.SendInputAsync(command + "\r");
                }
                else
                {
                    // No channel yet — put it back and wait for the next flush opportunity.
                    _pendingCommands.Enqueue(command);
                    return;
                }
            }
        }
        finally
        {
            _flushLock.Release();
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