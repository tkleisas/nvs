using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;

namespace NVS.ViewModels.Dock;

public partial class CallStackToolViewModel : Tool
{
    private readonly MainViewModel _main;
    private StackFrame? _selectedFrame;

    public ObservableCollection<StackFrame> Frames { get; } = [];

    public StackFrame? SelectedFrame
    {
        get => _selectedFrame;
        set
        {
            _selectedFrame = value;
            OnPropertyChanged();

            if (value?.Source is not null && value.Line > 0)
            {
                // Navigate to frame location in editor
                _ = NavigateToFrameAsync(value);
            }
        }
    }

    public CallStackToolViewModel(MainViewModel main)
    {
        _main = main;
        Id = "CallStack";
        Title = "📋 Call Stack";
        CanClose = false;
        CanPin = true;
    }

    public void UpdateFrames(IReadOnlyList<StackFrame> frames)
    {
        Frames.Clear();
        foreach (var frame in frames)
            Frames.Add(frame);

        if (Frames.Count > 0)
            SelectedFrame = Frames[0];
    }

    public void ClearFrames()
    {
        Frames.Clear();
        SelectedFrame = null;
    }

    private async Task NavigateToFrameAsync(StackFrame frame)
    {
        if (frame.Source is null) return;

        try
        {
            await _main.OpenFileAsync(frame.Source);
        }
        catch
        {
            // Best effort navigation
        }
    }
}
