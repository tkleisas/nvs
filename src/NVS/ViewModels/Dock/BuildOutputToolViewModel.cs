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
        Id = "BuildOutput";
        Title = "🔨 Build";
        CanClose = false;
        CanPin = true;
    }

    public void AppendOutput(string text, bool isError)
    {
        OutputLines.Add(new BuildOutputLine(text, isError));
    }

    [RelayCommand]
    private void Clear()
    {
        OutputLines.Clear();
    }
}

public sealed record BuildOutputLine(string Text, bool IsError);
