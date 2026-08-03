using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.ViewModels.Dock;

/// <summary>One node in the document symbols outline.</summary>
public partial class SymbolNodeViewModel : ObservableObject
{
    public required string Name { get; init; }
    public required SymbolKind Kind { get; init; }
    public required int Line { get; init; }
    public string? Detail { get; init; }
    public ObservableCollection<SymbolNodeViewModel> Children { get; } = [];

    public string Icon => Kind switch
    {
        SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Enum => "◆",
        SymbolKind.Method or SymbolKind.Function or SymbolKind.Constructor => "ƒ",
        SymbolKind.Property => "◈",
        SymbolKind.Field or SymbolKind.Variable or SymbolKind.Constant or SymbolKind.EnumMember => "●",
        SymbolKind.Namespace or SymbolKind.Module or SymbolKind.Package => "▣",
        _ => "○",
    };
}

/// <summary>
/// Document outline: symbols of the active document (Roslyn for C#, LSP otherwise),
/// refreshed on document switches and edits (debounced). Click to navigate.
/// </summary>
public partial class SymbolsToolViewModel : Tool
{
    private readonly MainViewModel _main;
    private readonly ILspSessionManager? _lspSessionManager;
    private DocumentViewModel? _trackedDocument;
    private CancellationTokenSource? _refreshCts;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "No symbols";

    [ObservableProperty]
    private SymbolNodeViewModel? _selectedSymbol;

    public ObservableCollection<SymbolNodeViewModel> Symbols { get; } = [];

    public SymbolsToolViewModel(MainViewModel main, ILspSessionManager? lspSessionManager = null)
    {
        _main = main;
        _lspSessionManager = lspSessionManager;
        Id = "Symbols";
        Title = "◈ Symbols";
        CanClose = true;
        CanPin = true;

        main.Editor!.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditorViewModel.ActiveDocument))
            {
                TrackActiveDocument();
                ScheduleRefresh();
            }
        };

        TrackActiveDocument();
        ScheduleRefresh();
    }

    private void TrackActiveDocument()
    {
        if (_trackedDocument is not null)
        {
            _trackedDocument.PropertyChanged -= OnDocumentPropertyChanged;
        }

        _trackedDocument = _main.Editor?.ActiveDocument;
        _retryCount = 0;

        if (_trackedDocument is not null)
        {
            _trackedDocument.PropertyChanged += OnDocumentPropertyChanged;
        }
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DocumentViewModel.Text))
        {
            ScheduleRefresh();
        }
    }

    private void ScheduleRefresh()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        var cts = _refreshCts = new CancellationTokenSource();
        _ = RefreshDebouncedAsync(cts.Token);
    }

    private async Task RefreshDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(600, ct);
            await RefreshAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct = default)
    {
        var doc = _main.Editor?.ActiveDocument;
        if (doc is null || _lspSessionManager is null)
        {
            Symbols.Clear();
            StatusText = doc is null ? "No document open" : "No language server";
            return;
        }

        IsLoading = true;
        try
        {
            var symbols = await _lspSessionManager.GetDocumentSymbolsAsync(doc.Document, ct);
            if (ct.IsCancellationRequested) return;

            Symbols.Clear();
            foreach (var symbol in symbols)
            {
                Symbols.Add(MapSymbol(symbol));
            }

            if (Symbols.Count == 0
                && doc.Document.Language == Language.CSharp
                && !_lspSessionManager.IsCSharpWorkspaceLoaded
                && _retryCount < MaxWorkspaceLoadRetries)
            {
                // C# symbols come from Roslyn; the workspace may still be loading.
                _retryCount++;
                StatusText = "Waiting for workspace to load…";
                _ = RetryRefreshLaterAsync();
            }
            else
            {
                StatusText = Symbols.Count == 0 ? "No symbols" : $"{CountSymbols(Symbols)} symbol(s)";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private const int MaxWorkspaceLoadRetries = 45; // ~3 min at 4s intervals — covers slow first-time solution loads
    private int _retryCount;

    private async Task RetryRefreshLaterAsync()
    {
        try
        {
            await Task.Delay(4000);
            await RefreshAsync();
        }
        catch (Exception)
        {
            // Best-effort retry; the next edit/switch triggers another refresh.
        }
    }

    private static int CountSymbols(IEnumerable<SymbolNodeViewModel> nodes) =>
        nodes.Sum(n => 1 + CountSymbols(n.Children));

    private static SymbolNodeViewModel MapSymbol(DocumentSymbol symbol)
    {
        var node = new SymbolNodeViewModel
        {
            Name = symbol.Name,
            Kind = symbol.Kind,
            Line = symbol.SelectionRange.Start.Line + 1, // LSP 0-based → editor 1-based
            Detail = symbol.Detail,
        };

        foreach (var child in symbol.Children)
        {
            node.Children.Add(MapSymbol(child));
        }

        return node;
    }

    [RelayCommand]
    private void NavigateToSymbol(SymbolNodeViewModel? node)
    {
        if (node is null) return;

        if (_main.Editor?.ActiveDocument is { } doc)
        {
            doc.CursorLine = node.Line;
            doc.CursorColumn = 1;
            _main.ActivateEditorDocument();
        }
    }
}
