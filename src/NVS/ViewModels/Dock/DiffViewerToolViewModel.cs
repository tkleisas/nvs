using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;
using NVS.Services.Git;

namespace NVS.ViewModels.Dock;

public sealed partial class DiffViewerToolViewModel : Tool
{
    // Value converters for AXAML bindings
    public static readonly IValueConverter IsDeletedConverter = new DiffLineTypeConverter(DiffSideLineType.Deleted);
    public static readonly IValueConverter IsAddedConverter = new DiffLineTypeConverter(DiffSideLineType.Added);
    public static readonly IValueConverter IsEmptyConverter = new DiffLineTypeConverter(DiffSideLineType.Empty);

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

    private bool _isStaged;
    public bool IsStaged
    {
        get => _isStaged;
        set
        {
            if (_isStaged != value)
            {
                _isStaged = value;
                OnPropertyChanged(nameof(IsStaged));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string DisplayTitle => string.IsNullOrEmpty(FilePath)
        ? "Diff Viewer"
        : $"{System.IO.Path.GetFileName(FilePath)} ({(IsStaged ? "Staged" : "Working Tree")})";

    public ObservableCollection<DiffSidePair> DiffLines { get; } = [];
    public ObservableCollection<DiffHunk> Hunks { get; } = [];

    private int _additions;
    public int Additions
    {
        get => _additions;
        set { if (_additions != value) { _additions = value; OnPropertyChanged(nameof(Additions)); } }
    }

    private int _deletions;
    public int Deletions
    {
        get => _deletions;
        set { if (_deletions != value) { _deletions = value; OnPropertyChanged(nameof(Deletions)); } }
    }

    public DiffViewerToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "DiffViewer";
        Title = "📊 Diff Viewer";
        CanClose = false;
        CanPin = true;
    }

    public void LoadDiff(string filePath, IReadOnlyList<DiffHunk> hunks, bool isStaged)
    {
        FilePath = filePath;
        IsStaged = isStaged;
        Title = $"📊 {DisplayTitle}";

        Hunks.Clear();
        DiffLines.Clear();

        foreach (var h in hunks)
            Hunks.Add(h);

        var aligned = DiffAligner.AlignHunks(hunks);
        foreach (var pair in aligned)
            DiffLines.Add(pair);

        Additions = aligned.Count(p => p.Right.Type == DiffSideLineType.Added);
        Deletions = aligned.Count(p => p.Left.Type == DiffSideLineType.Deleted);
    }

    [RelayCommand]
    private void Clear()
    {
        DiffLines.Clear();
        Hunks.Clear();
        FilePath = "";
        Title = "📊 Diff Viewer";
        Additions = 0;
        Deletions = 0;
    }
}

public sealed class DiffLineTypeConverter : IValueConverter
{
    private readonly DiffSideLineType _targetType;

    public DiffLineTypeConverter(DiffSideLineType targetType)
    {
        _targetType = targetType;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is DiffSideLineType type && type == _targetType;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
