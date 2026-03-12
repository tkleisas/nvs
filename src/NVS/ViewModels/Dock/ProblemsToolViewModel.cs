using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;

namespace NVS.ViewModels.Dock;

public partial class ProblemsToolViewModel : Tool
{
    private readonly MainViewModel _main;

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
        Problems.Clear();

        foreach (var error in errors)
        {
            Problems.Add(new ProblemItem
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
            Problems.Add(new ProblemItem
            {
                Severity = "Warning",
                SeverityIcon = "⚠️",
                Message = warning.Message,
                FilePath = warning.FilePath,
                Line = warning.Line,
                Column = warning.Column
            });
        }

        Title = Problems.Count > 0
            ? $"⚠ Problems ({Problems.Count})"
            : "⚠ Problems";
    }

    [RelayCommand]
    private void ClearProblems()
    {
        Problems.Clear();
        Title = "⚠ Problems";
    }

    [RelayCommand]
    private async Task NavigateToProblem(ProblemItem? problem)
    {
        if (problem?.FilePath is null) return;

        try
        {
            var editorService = _main.EditorService;
            await editorService.OpenDocumentAsync(problem.FilePath);

            if (problem.Line.HasValue)
            {
                // EditorViewModel handles cursor positioning via event
            }
        }
        catch (Exception)
        {
            // Ignore navigation errors
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
