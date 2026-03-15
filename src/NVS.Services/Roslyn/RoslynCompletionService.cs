using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using NVS.Core.Interfaces;
using Serilog;
using CompletionItem = NVS.Core.Interfaces.CompletionItem;
using CompletionItemKind = NVS.Core.Interfaces.CompletionItemKind;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

namespace NVS.Services.Roslyn;

/// <summary>
/// Provides C# completions using Roslyn's MSBuildWorkspace and CompletionService.
/// Handles solution/project loading, document synchronization, and completion generation.
/// </summary>
public sealed class RoslynCompletionService : IRoslynCompletionService
{
    private static readonly ILogger Logger = Log.ForContext<RoslynCompletionService>();

    private static readonly object LocatorLock = new();
    private static bool _locatorRegistered;

    private MSBuildWorkspace? _workspace;
    private RoslynSolution? _solution;
    private bool _disposed;

    // Cache: file path → DocumentId for fast lookup
    private readonly ConcurrentDictionary<string, DocumentId> _documentMap = new(StringComparer.OrdinalIgnoreCase);

    public bool IsWorkspaceLoaded => _solution is not null;

    public async Task LoadWorkspaceAsync(string solutionOrProjectPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureMSBuildRegistered();

        // Unload previous workspace if any
        UnloadWorkspace();

        Logger.Information("[Roslyn] Loading workspace from {Path}", solutionOrProjectPath);

        _workspace = CreateWorkspace();

        _workspace.WorkspaceFailed += (_, args) =>
            Logger.Warning("[Roslyn] Workspace diagnostic: {Kind} — {Message}", args.Diagnostic.Kind, args.Diagnostic.Message);

        var ext = Path.GetExtension(solutionOrProjectPath);
        if (ext is ".slnx")
        {
            // MSBuildWorkspace doesn't support .slnx — parse XML to extract project paths
            await LoadSlnxAsync(solutionOrProjectPath, cancellationToken).ConfigureAwait(false);
        }
        else if (ext is ".sln")
        {
            _solution = await _workspace.OpenSolutionAsync(solutionOrProjectPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        else if (ext is ".csproj")
        {
            var project = await _workspace.OpenProjectAsync(solutionOrProjectPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _solution = project.Solution;
        }
        else
        {
            Logger.Warning("[Roslyn] Unsupported workspace file: {Path}", solutionOrProjectPath);
            return;
        }

        // Build document path → DocumentId index
        RebuildDocumentMap();

        var projectCount = _solution?.Projects.Count() ?? 0;
        var docCount = _documentMap.Count;
        Logger.Information("[Roslyn] Workspace loaded: {Projects} project(s), {Documents} document(s)", projectCount, docCount);
    }

    private async Task LoadSlnxAsync(string slnxPath, CancellationToken cancellationToken)
    {
        var solutionDir = Path.GetDirectoryName(slnxPath)!;
        var projectPaths = ParseSlnxProjectPaths(slnxPath, solutionDir);

        if (projectPaths.Count == 0)
        {
            Logger.Warning("[Roslyn] No projects found in .slnx: {Path}", slnxPath);
            return;
        }

        Logger.Information("[Roslyn] Parsed .slnx: {Count} project(s)", projectPaths.Count);

        foreach (var projectPath in projectPaths)
        {
            try
            {
                var project = await _workspace!.OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _solution = project.Solution;
                Logger.Debug("[Roslyn] Loaded project: {Path}", projectPath);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "[Roslyn] Failed to load project: {Path}", projectPath);
            }
        }
    }

    private static List<string> ParseSlnxProjectPaths(string slnxPath, string solutionDir)
    {
        var paths = new List<string>();
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(slnxPath);
            var projectElements = doc.Descendants("Project");
            foreach (var el in projectElements)
            {
                var pathAttr = el.Attribute("Path")?.Value;
                if (pathAttr is not null)
                {
                    var fullPath = Path.GetFullPath(Path.Combine(solutionDir, pathAttr));
                    if (File.Exists(fullPath))
                        paths.Add(fullPath);
                    else
                        Log.Warning("[Roslyn] Project not found: {Path}", fullPath);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Roslyn] Failed to parse .slnx: {Path}", slnxPath);
        }
        return paths;
    }

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        if (_solution is null)
            return [];

        var document = FindDocument(filePath);
        if (document is null)
        {
            Logger.Debug("[Roslyn] Document not found for {Path}", filePath);
            return [];
        }

        var completionService = CompletionService.GetService(document);
        if (completionService is null)
        {
            Logger.Warning("[Roslyn] CompletionService unavailable for {Path}", filePath);
            return [];
        }

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var position = GetOffset(text, line, column);

        if (position < 0 || position > text.Length)
            return [];

        var completionList = await completionService.GetCompletionsAsync(
            document, position, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (completionList is null)
            return [];

        var results = new List<CompletionItem>(completionList.ItemsList.Count);
        foreach (var item in completionList.ItemsList)
        {
            results.Add(MapCompletionItem(item));
        }

        Logger.Debug("[Roslyn] Returned {Count} completions for {Path}:{Line}:{Col}",
            results.Count, Path.GetFileName(filePath), line, column);

        return results;
    }

    public void UpdateDocumentContent(string filePath, string content)
    {
        if (_solution is null || _workspace is null)
            return;

        if (!_documentMap.TryGetValue(NormalizePath(filePath), out var docId))
            return;

        var sourceText = SourceText.From(content);
        _solution = _solution.WithDocumentText(docId, sourceText);
    }

    public void UnloadWorkspace()
    {
        if (_workspace is not null)
        {
            _workspace.CloseSolution();
            _workspace.Dispose();
            _workspace = null;
        }
        _solution = null;
        _documentMap.Clear();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        UnloadWorkspace();
        return ValueTask.CompletedTask;
    }

    // ─── Private Helpers ────────────────────────────────────────────────────

    private static void EnsureMSBuildRegistered()
    {
        if (_locatorRegistered) return;
        lock (LocatorLock)
        {
            if (_locatorRegistered) return;
            MSBuildLocator.RegisterDefaults();
            _locatorRegistered = true;
            Logger.Information("[Roslyn] MSBuild registered: {Version}",
                MSBuildLocator.QueryVisualStudioInstances().FirstOrDefault()?.Version);
        }
    }

    // Separate method to avoid loading MSBuildWorkspace type before MSBuild is registered
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MSBuildWorkspace CreateWorkspace()
    {
        return MSBuildWorkspace.Create();
    }

    private void RebuildDocumentMap()
    {
        _documentMap.Clear();
        if (_solution is null) return;

        foreach (var project in _solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is not null)
                {
                    var key = NormalizePath(document.FilePath);
                    _documentMap.TryAdd(key, document.Id);
                }
            }
        }
    }

    private Document? FindDocument(string filePath)
    {
        if (_solution is null) return null;

        var key = NormalizePath(filePath);
        if (_documentMap.TryGetValue(key, out var docId))
            return _solution.GetDocument(docId);

        return null;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('/', '\\');

    private static int GetOffset(SourceText text, int line, int column)
    {
        // NVS uses 0-based line and column
        if (line < 0 || line >= text.Lines.Count)
            return -1;

        var textLine = text.Lines[line];
        return textLine.Start + Math.Min(column, textLine.Span.Length);
    }

    private static CompletionItem MapCompletionItem(Microsoft.CodeAnalysis.Completion.CompletionItem item)
    {
        return new CompletionItem
        {
            Label = item.DisplayText,
            InsertText = item.DisplayText,
            Detail = item.InlineDescription,
            Kind = MapKind(item.Tags),
            IsSnippet = item.Tags.Contains("Snippet"),
        };
    }

    private static CompletionItemKind MapKind(ImmutableArray<string> tags)
    {
        if (tags.Contains("Method") || tags.Contains("ExtensionMethod"))
            return CompletionItemKind.Method;
        if (tags.Contains("Class"))
            return CompletionItemKind.Class;
        if (tags.Contains("Interface"))
            return CompletionItemKind.Interface;
        if (tags.Contains("Property"))
            return CompletionItemKind.Property;
        if (tags.Contains("Field"))
            return CompletionItemKind.Field;
        if (tags.Contains("Local") || tags.Contains("Parameter"))
            return CompletionItemKind.Variable;
        if (tags.Contains("Keyword"))
            return CompletionItemKind.Keyword;
        if (tags.Contains("Namespace") || tags.Contains("Module"))
            return CompletionItemKind.Module;
        if (tags.Contains("Constructor"))
            return CompletionItemKind.Constructor;
        if (tags.Contains("Snippet"))
            return CompletionItemKind.Snippet;
        if (tags.Contains("Delegate") || tags.Contains("Event"))
            return CompletionItemKind.Function;
        if (tags.Contains("Structure") || tags.Contains("Enum") || tags.Contains("EnumMember"))
            return CompletionItemKind.Class;
        return CompletionItemKind.Text;
    }
}
