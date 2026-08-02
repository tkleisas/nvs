using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;

namespace NVS.ViewModels.Dock;

public partial class ProblemsToolViewModel : Tool
{
    private readonly MainViewModel _main;
    private readonly List<ProblemItem> _buildProblems = [];
    private readonly Dictionary<string, List<ProblemItem>> _lspProblemsByFile = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ProblemItem> Problems { get; } = [];

    public ProblemsToolViewModel(MainViewModel main)
    {
        _main = main;
        Id = "Problems";
        Title = "⚠ Problems";
        CanClose = false;
        CanPin = true;
    }

    public void SetProblems(IReadOnlyList<BuildError> errors, IReadOnlyList<BuildWarning> warnings)
    {
        _buildProblems.Clear();

        foreach (var error in errors)
        {
            _buildProblems.Add(new ProblemItem
            {
                Severity = "Error",
                SeverityIcon = "❌",
                Message = error.Message,
                FilePath = error.FilePath,
                Line = error.Line,
                Column = error.Column
            });
        }

        foreach (var warning in warnings)
        {
            _buildProblems.Add(new ProblemItem
            {
                Severity = "Warning",
                SeverityIcon = "⚠️",
                Message = warning.Message,
                FilePath = warning.FilePath,
                Line = warning.Line,
                Column = warning.Column
            });
        }

        RebuildList();
    }

    /// <summary>
    /// Merges live LSP diagnostics for one file into the panel. Entries for the file are
    /// replaced on each call; an empty list clears them.
    /// </summary>
    public void SetLspDiagnostics(string filePath, IReadOnlyList<Diagnostic> diagnostics)
    {
        var items = diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(d => new ProblemItem
            {
                Severity = d.Severity == DiagnosticSeverity.Error ? "Error" : "Warning",
                SeverityIcon = d.Severity == DiagnosticSeverity.Error ? "❌" : "⚠️",
                Message = d.Message,
                FilePath = filePath,
                Line = d.Range.Start.Line + 1,
                Column = d.Range.Start.Column + 1
            })
            .ToList();

        if (items.Count == 0)
        {
            _lspProblemsByFile.Remove(filePath);
        }
        else
        {
            _lspProblemsByFile[filePath] = items;
        }

        RebuildList();
    }

    private void RebuildList()
    {
        Problems.Clear();
        foreach (var problem in _buildProblems)
        {
            Problems.Add(problem);
        }
        foreach (var fileProblems in _lspProblemsByFile.Values)
        {
            foreach (var problem in fileProblems)
            {
                Problems.Add(problem);
            }
        }

        Title = Problems.Count > 0
            ? $"⚠ Problems ({Problems.Count})"
            : "⚠ Problems";
    }

    [RelayCommand]
    private void ClearProblems()
    {
        _buildProblems.Clear();
        _lspProblemsByFile.Clear();
        Problems.Clear();
        Title = "⚠ Problems";
    }

    [RelayCommand]
    private async Task CopyProblem(ProblemItem? problem)
    {
        if (problem is null) return;

        var text = problem.FilePath is not null
            ? $"{problem.Severity}: {problem.Message} [{problem.Location}]"
            : $"{problem.Severity}: {problem.Message}";

        var clipboard = (Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Clipboard;

        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }

    [RelayCommand]
    private async Task NavigateToProblem(ProblemItem? problem)
    {
        if (problem?.FilePath is null) return;

        try
        {
            await _main.EditorService.OpenDocumentAsync(problem.FilePath);

            var editor = _main.Editor;
            if (editor is null) return;

            var docVm = editor.OpenDocuments.FirstOrDefault(d =>
                string.Equals(d.Document.FilePath, problem.FilePath, StringComparison.OrdinalIgnoreCase));
            if (docVm is null) return;

            editor.ActiveDocument = docVm;
            if (problem.Line.HasValue)
            {
                docVm.CursorLine = problem.Line.Value;
                docVm.CursorColumn = problem.Column ?? 1;
            }

            _main.ActivateEditorDocument();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to navigate to problem in {Path}", problem.FilePath);
        }
    }
}

public sealed class ProblemItem
{
    public required string Severity { get; init; }
    public required string SeverityIcon { get; init; }
    public required string Message { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }

    public string Location => FilePath is not null
        ? $"{Path.GetFileName(FilePath)}({Line},{Column})"
        : string.Empty;
}
