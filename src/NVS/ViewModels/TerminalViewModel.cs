using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.ViewModels;

/// <summary>
/// Terminal panel visibility and I/O, owned by <see cref="MainViewModel"/>
/// and exposed to views as <c>Main.Terminal</c>.
/// </summary>
public sealed partial class TerminalViewModel : ObservableObject
{
    private readonly ITerminalService _terminalService;
    private readonly MainViewModel _main;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _output = "";

    [ObservableProperty]
    private string _input = "";

    public TerminalViewModel(ITerminalService terminalService, MainViewModel main)
    {
        _terminalService = terminalService;
        _main = main;
    }

    [RelayCommand]
    private void Toggle()
    {
        IsVisible = !IsVisible;
        if (IsVisible && _terminalService.ActiveTerminal is null)
        {
            CreateNewTerminal();
        }
    }

    [RelayCommand]
    private void SendInput()
    {
        if (string.IsNullOrEmpty(Input)) return;

        var terminal = _terminalService.ActiveTerminal;
        if (terminal is null)
        {
            CreateNewTerminal();
            terminal = _terminalService.ActiveTerminal;
        }
        if (terminal is null) return;

        terminal.WriteLine(Input);
        Input = "";
    }

    [RelayCommand]
    private void NewTerminal()
    {
        CreateNewTerminal();
    }

    [RelayCommand]
    private void CloseTerminal()
    {
        var active = _terminalService.ActiveTerminal;
        if (active is not null)
        {
            _terminalService.CloseTerminal(active);
        }

        if (_terminalService.Terminals.Count == 0)
        {
            IsVisible = false;
            Output = "";
        }
    }

    private void CreateNewTerminal()
    {
        var terminal = _terminalService.CreateTerminal(new TerminalOptions
        {
            Name = "Terminal",
            WorkingDirectory = _main.WorkspacePath,
        });

        terminal.DataReceived += (_, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Output += e.Data;
            });
        };

        Output = "";
        IsVisible = true;
        _main.StatusMessage = "Terminal opened";
    }
}
