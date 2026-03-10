using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IEditorService _editorService;
    private readonly IGitService _gitService;
    private readonly ITerminalService _terminalService;

    private string _title = "NVS - No Vim Substitute";
    private bool _isWorkspaceOpen;
    private string? _workspacePath;
    private string _statusMessage = "Ready";
    private string _currentBranch = "main";
    private EditorViewModel? _editor;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(
        IWorkspaceService workspaceService,
        IEditorService editorService,
        IGitService gitService,
        ITerminalService terminalService)
    {
        _workspaceService = workspaceService;
        _editorService = editorService;
        _gitService = gitService;
        _terminalService = terminalService;
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public bool IsWorkspaceOpen
    {
        get => _isWorkspaceOpen;
        set => SetProperty(ref _isWorkspaceOpen, value);
    }

    public string? WorkspacePath
    {
        get => _workspacePath;
        set => SetProperty(ref _workspacePath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentBranch
    {
        get => _currentBranch;
        set => SetProperty(ref _currentBranch, value);
    }

    public EditorViewModel? Editor
    {
        get => _editor;
        set => SetProperty(ref _editor, value);
    }

    public ObservableCollection<FileTreeNode> FileTree { get; } = [];

    [RelayCommand]
    private async Task OpenFolder()
    {
        // TODO: Implement folder picker dialog
        StatusMessage = "Open folder...";
    }

    [RelayCommand]
    private async Task NewFile()
    {
        if (Editor != null)
        {
            await Editor.NewFileCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        if (Editor != null)
        {
            await Editor.OpenFileCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task SaveFile()
    {
        if (Editor != null)
        {
            await Editor.SaveFileCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task SaveAll()
    {
        if (Editor != null)
        {
            await Editor.SaveAllCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void CloseFile()
    {
        Editor?.CloseFileCommand.Execute(null);
    }

    [RelayCommand]
    private void ToggleTerminal()
    {
        StatusMessage = "Terminal toggled";
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        StatusMessage = "Sidebar toggled";
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class FileTreeNode
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsDirectory { get; init; }
    public ObservableCollection<FileTreeNode> Children { get; } = [];
}
