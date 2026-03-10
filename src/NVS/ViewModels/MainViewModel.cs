using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NVS.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "NVS - No Vim Substitute";

    [ObservableProperty]
    private bool _isWorkspaceOpen;

    [ObservableProperty]
    private string? _workspacePath;

    [ObservableProperty]
    private int _activeTabIndex = -1;

    public IReadOnlyList<string> OpenFiles { get; } = new List<string>();

    public string StatusMessage { get; } = "Ready";
}
