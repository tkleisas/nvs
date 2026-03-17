using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;
using NVS.Services.Git;

namespace NVS.ViewModels.Dock;

public sealed partial class ConflictResolverToolViewModel : Tool
{
    public MainViewModel Main { get; }

    private string _filePath = "";
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                _filePath = value;
                OnPropertyChanged(nameof(FilePath));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string DisplayTitle => string.IsNullOrEmpty(FilePath)
        ? "Conflict Resolver"
        : $"Conflicts: {System.IO.Path.GetFileName(FilePath)}";

    public ObservableCollection<ConflictBlockViewModel> Conflicts { get; } = [];

    private string _fileContent = "";

    public ConflictResolverToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "ConflictResolver";
        Title = "⚡ Conflict Resolver";
        CanClose = false;
        CanPin = true;
    }

    public void LoadFile(string filePath, string content)
    {
        FilePath = filePath;
        _fileContent = content;
        Title = $"⚡ {DisplayTitle}";

        Conflicts.Clear();
        var blocks = ConflictParser.Parse(content);
        foreach (var block in blocks)
        {
            Conflicts.Add(new ConflictBlockViewModel(block));
        }
    }

    [RelayCommand]
    private async Task ResolveAll()
    {
        if (string.IsNullOrEmpty(FilePath) || Conflicts.Count == 0) return;

        var resolutions = new Dictionary<int, ConflictResolution>();
        foreach (var c in Conflicts)
        {
            if (c.Resolution is not null)
                resolutions[c.Block.StartLine] = c.Resolution.Value;
        }

        var resolved = ConflictParser.ResolveConflicts(_fileContent, resolutions);
        await System.IO.File.WriteAllTextAsync(FilePath, resolved);

        var result = await Main.GitServiceAccessor.MarkResolvedAsync(FilePath);
        Main.StatusMessage = result.Success
            ? $"Resolved: {System.IO.Path.GetFileName(FilePath)}"
            : $"Mark resolved failed: {result.ErrorMessage}";

        Conflicts.Clear();
    }
}

public sealed class ConflictBlockViewModel : System.ComponentModel.INotifyPropertyChanged
{
    public ConflictBlock Block { get; }
    public string OursText => string.Join('\n', Block.OursLines);
    public string TheirsText => string.Join('\n', Block.TheirsLines);
    public string OursLabel => Block.OursLabel ?? "Current";
    public string TheirsLabel => Block.TheirsLabel ?? "Incoming";

    private ConflictResolution? _resolution;
    public ConflictResolution? Resolution
    {
        get => _resolution;
        set
        {
            if (_resolution != value)
            {
                _resolution = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Resolution)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsResolved)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ResolutionLabel)));
            }
        }
    }

    public bool IsResolved => _resolution is not null;
    public string ResolutionLabel => _resolution switch
    {
        ConflictResolution.AcceptCurrent => "✓ Current",
        ConflictResolution.AcceptIncoming => "✓ Incoming",
        ConflictResolution.AcceptBoth => "✓ Both",
        _ => "Unresolved",
    };

    public ConflictBlockViewModel(ConflictBlock block)
    {
        Block = block;
    }

    public void AcceptCurrent() => Resolution = ConflictResolution.AcceptCurrent;
    public void AcceptIncoming() => Resolution = ConflictResolution.AcceptIncoming;
    public void AcceptBoth() => Resolution = ConflictResolution.AcceptBoth;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
