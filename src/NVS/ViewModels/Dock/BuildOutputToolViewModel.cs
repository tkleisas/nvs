using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Helpers;

namespace NVS.ViewModels.Dock;

public partial class BuildOutputToolViewModel : Tool
{
    private readonly MainViewModel _main;
    private readonly object _pendingLock = new();
    private readonly List<BuildOutputLine> _pending = [];
    private int _flushPending;

    public BatchedObservableCollection<BuildOutputLine> OutputLines { get; } = [];

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

    /// <summary>
    /// Queues an output line. Lines reach <see cref="OutputLines"/> in coalesced
    /// batches (at most one collection change per UI pump cycle) so verbose builds
    /// don't flood the binding layer with per-line notifications.
    /// </summary>
    public void AppendOutput(string text, bool isError)
    {
        lock (_pendingLock)
        {
            _pending.Add(new BuildOutputLine(text, isError));
            if (_flushPending != 0)
            {
                return;
            }
            _flushPending = 1;
        }

        // Without a running Avalonia application (unit tests) there is no UI
        // dispatcher to schedule on — callers/tests flush explicitly instead.
        if (Avalonia.Application.Current is not null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(FlushPending);
        }
    }

    /// <summary>Drains queued lines into <see cref="OutputLines"/> as one batch.</summary>
    internal void FlushPending()
    {
        List<BuildOutputLine> batch;
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
            {
                _flushPending = 0;
                return;
            }

            batch = [.. _pending];
            _pending.Clear();
            _flushPending = 0;
        }

        OutputLines.AddRange(batch);
    }

    public void ClearOutput()
    {
        lock (_pendingLock)
        {
            _pending.Clear();
        }
        OutputLines.Clear();
    }

    [RelayCommand]
    private void Clear()
    {
        ClearOutput();
    }
}

public sealed record BuildOutputLine(string Text, bool IsError);
