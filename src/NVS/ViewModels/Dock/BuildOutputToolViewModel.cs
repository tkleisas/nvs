using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;

namespace NVS.ViewModels.Dock;

public partial class BuildOutputToolViewModel : Tool
{
    private readonly MainViewModel _main;

    public ObservableCollection<BuildOutputLine> OutputLines { get; } = [];

    public BuildOutputToolViewModel(MainViewModel main)
    {
        _main = main;
        _main.BuildRun.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(BuildRunViewModel.IsBuilding))
            {
                OnPropertyChanged(nameof(IsBuilding));
            }
        };
        Id = "BuildOutput";
        Title = "🔨 Build";
        CanClose = false;
        CanPin = true;
    }

    /// <summary>Whether a build is currently running (drives the busy indicator).</summary>
    public bool IsBuilding => _main.BuildRun.IsBuilding;

    public void AppendOutput(string text, bool isError)
    {
        OutputLines.Add(new BuildOutputLine(text, isError));
    }

    public void ClearOutput()
    {
        OutputLines.Clear();
    }

    [RelayCommand]
    private void Clear()
    {
        ClearOutput();
    }
}

public sealed record BuildOutputLine(string Text, bool IsError);
