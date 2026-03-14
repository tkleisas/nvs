using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;

namespace NVS.ViewModels.Dock;

public sealed partial class CodeMetricsToolViewModel : Tool, INotifyPropertyChanged
{
    private readonly MainViewModel _main;
    private bool _isAnalyzing;
    private string _statusMessage = "Ready to analyze";

    public ObservableCollection<MetricTreeNode> MetricTree { get; } = [];

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set { _isAnalyzing = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public CodeMetricsToolViewModel(MainViewModel main)
    {
        _main = main;
        Id = "CodeMetrics";
        Title = "📊 Code Metrics";
        CanClose = true;
        CanPin = true;
    }

    [RelayCommand]
    private async Task AnalyzeProject()
    {
        if (IsAnalyzing) return;

        var workspacePath = _main.WorkspacePath;
        if (string.IsNullOrEmpty(workspacePath)) return;

        IsAnalyzing = true;
        StatusMessage = "Analyzing...";
        MetricTree.Clear();

        try
        {
            var service = _main.CodeMetricsService;
            if (service is null)
            {
                StatusMessage = "Code metrics service not available";
                return;
            }

            // Find all .csproj files in workspace
            var projects = Directory.EnumerateFiles(workspacePath, "*.csproj", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToList();

            var totalTypes = 0;
            var totalMethods = 0;

            foreach (var project in projects)
            {
                var projectMetrics = await service.CalculateProjectMetricsAsync(project);
                BuildMetricTree(projectMetrics);
                totalTypes += projectMetrics.TotalTypes;
                totalMethods += projectMetrics.TotalMethods;
            }

            StatusMessage = $"Analyzed {projects.Count} project(s), {totalTypes} types, {totalMethods} methods";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    [RelayCommand]
    private async Task AnalyzeCurrentFile()
    {
        if (IsAnalyzing) return;

        var activeDoc = _main.Editor?.ActiveDocument;
        if (activeDoc?.Document.FilePath is null) return;

        if (!activeDoc.Document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Code metrics only available for C# files";
            return;
        }

        IsAnalyzing = true;
        StatusMessage = "Analyzing file...";
        MetricTree.Clear();

        try
        {
            var service = _main.CodeMetricsService;
            if (service is null)
            {
                StatusMessage = "Code metrics service not available";
                return;
            }

            var fileMetrics = await service.CalculateFileMetricsAsync(activeDoc.Document.FilePath);
            var fileNode = CreateFileNode(fileMetrics);
            MetricTree.Add(fileNode);
            StatusMessage = $"Analyzed {fileMetrics.Types.Count} types";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    public async Task NavigateToFile(string filePath, int line)
    {
        await _main.OpenFileAsync(filePath);
        if (_main.Editor?.ActiveDocument is { } doc)
        {
            doc.CursorLine = line;
        }
    }

    private void BuildMetricTree(ProjectMetrics projectMetrics)
    {
        var projectNode = new MetricTreeNode
        {
            Name = projectMetrics.ProjectName,
            Icon = "📦",
            Detail = $"{projectMetrics.TotalLines.Total} lines, {projectMetrics.TotalTypes} types, {projectMetrics.TotalMethods} methods",
            MetricValue = $"Avg CC: {projectMetrics.AverageCyclomaticComplexity:F1} | MI: {projectMetrics.AverageMaintainabilityIndex:F0}",
            Severity = GetMaintainabilitySeverity(projectMetrics.AverageMaintainabilityIndex),
        };

        foreach (var file in projectMetrics.Files.OrderBy(f => f.FileName))
        {
            if (file.Types.Count == 0) continue;
            projectNode.Children.Add(CreateFileNode(file));
        }

        MetricTree.Add(projectNode);
    }

    private static MetricTreeNode CreateFileNode(FileMetrics file)
    {
        var fileNode = new MetricTreeNode
        {
            Name = file.FileName,
            FilePath = file.FilePath,
            Icon = "📄",
            Detail = $"{file.Lines.Total} lines ({file.Lines.Executable} executable)",
        };

        foreach (var type in file.Types)
        {
            var typeNode = new MetricTreeNode
            {
                Name = type.Name,
                FilePath = file.FilePath,
                Line = type.Line,
                Icon = type.Kind switch
                {
                    TypeKindMetric.Interface => "🔷",
                    TypeKindMetric.Enum => "🔶",
                    TypeKindMetric.Record => "📋",
                    TypeKindMetric.Struct => "🟩",
                    _ => "🟦",
                },
                Detail = $"Coupling: {type.ClassCoupling} | DOI: {type.DepthOfInheritance}",
            };

            foreach (var method in type.Methods)
            {
                var severity = GetComplexitySeverity(method.CyclomaticComplexity);
                var methodNode = new MetricTreeNode
                {
                    Name = method.Name,
                    FilePath = file.FilePath,
                    Line = method.Line,
                    Icon = severity switch
                    {
                        MetricSeverity.Good => "🟢",
                        MetricSeverity.Warning => "🟡",
                        MetricSeverity.Danger => "🔴",
                        _ => "⚪",
                    },
                    Detail = $"CC: {method.CyclomaticComplexity} | Lines: {method.Lines.Executable}",
                    MetricValue = $"MI: {method.MaintainabilityIndex:F0}",
                    Severity = severity,
                };
                typeNode.Children.Add(methodNode);
            }

            fileNode.Children.Add(typeNode);
        }

        return fileNode;
    }

    private static MetricSeverity GetComplexitySeverity(int complexity) => complexity switch
    {
        <= 5 => MetricSeverity.Good,
        <= 10 => MetricSeverity.Warning,
        _ => MetricSeverity.Danger,
    };

    private static MetricSeverity GetMaintainabilitySeverity(double mi) => mi switch
    {
        >= 60 => MetricSeverity.Good,
        >= 40 => MetricSeverity.Warning,
        _ => MetricSeverity.Danger,
    };

    private new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
    }
}

public sealed class MetricTreeNode : INotifyPropertyChanged
{
    public string Name { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Detail { get; init; } = "";
    public string MetricValue { get; init; } = "";
    public string? FilePath { get; init; }
    public int Line { get; init; }
    public MetricSeverity Severity { get; init; }

    public ObservableCollection<MetricTreeNode> Children { get; } = [];

#pragma warning disable CS0067
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
}

public enum MetricSeverity
{
    None,
    Good,
    Warning,
    Danger,
}
