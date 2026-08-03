using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;

namespace NVS.ViewModels;

/// <summary>
/// Workspace-wide text search state and commands, owned by
/// <see cref="MainViewModel"/> and exposed to views as <c>Main.Search</c>.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int MaxResults = 200;

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", "__pycache__", ".vs", ".idea",
        "packages", "TestResults", ".nuget", "dist", "build", ".cache",
    };

    private readonly IFileSystemService _fileSystemService;
    private readonly MainViewModel _main;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _wholeWord;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private string _replaceText = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    public ObservableCollection<FileSearchResult> Results { get; } = [];

    public SearchViewModel(IFileSystemService fileSystemService, MainViewModel main)
    {
        _fileSystemService = fileSystemService;
        _main = main;
    }

    [RelayCommand]
    private async Task SearchFiles()
    {
        if (string.IsNullOrWhiteSpace(Query) || _main.WorkspacePath is not { } workspacePath) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        Results.Clear();
        var query = Query;

        Func<string, bool> matcher;
        try
        {
            matcher = BuildMatcher(query, MatchCase, WholeWord, UseRegex);
        }
        catch (ArgumentException ex)
        {
            _main.StatusMessage = $"Search: invalid regular expression ({ex.Message})";
            IsSearching = false;
            return;
        }

        var resultsCapped = false;

        try
        {
            IReadOnlyList<string> files;
            try
            {
                files = await _fileSystemService.GetFilesAsync(workspacePath, "*", recursive: true, token);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                files = await Task.Run(() => EnumerateFilesSafe(workspacePath), token);
            }

            var batch = new List<FileSearchResult>();

            foreach (var file in files)
            {
                if (token.IsCancellationRequested) break;
                if (IsInExcludedDirectory(file) || IsBinaryExtension(file)) continue;

                try
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Exists && fileInfo.Length > 5 * 1024 * 1024) continue;
                    }
                    catch { /* If we can't check size, try reading anyway */ }

                    var content = await _fileSystemService.ReadAllTextAsync(file, token);
                    var lines = content.Split('\n');
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (matcher(lines[i]))
                        {
                            batch.Add(new FileSearchResult
                            {
                                FilePath = file,
                                RelativePath = Path.GetRelativePath(workspacePath, file),
                                LineNumber = i + 1,
                                LineText = lines[i].Trim(),
                            });

                            if (batch.Count >= MaxResults)
                            {
                                resultsCapped = true;
                                break;
                            }
                        }
                    }
                    if (resultsCapped) break;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Skip files that can't be read
                }
            }

            foreach (var result in batch)
                Results.Add(result);

            _main.StatusMessage = resultsCapped
                ? $"Search: showing first {MaxResults} result(s) for \"{query}\" (refine your query for more)"
                : $"Search: {Results.Count} result(s) for \"{query}\"";
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled
        }
        catch (Exception ex)
        {
            _main.StatusMessage = $"Search error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task OpenSearchResult(FileSearchResult? result)
    {
        if (result is null) return;
        await _main.OpenFileAsync(result.FilePath);
        if (_main.Editor?.ActiveDocument is { } doc)
        {
            doc.CursorLine = result.LineNumber;
        }
    }

    /// <summary>
    /// Confirmation callback for bulk replace: (matchCount, fileCount) → proceed?
    /// Set by the view; when null, replacement proceeds without asking.
    /// </summary>
    public Func<int, int, Task<bool>>? ConfirmReplaceAll { get; set; }

    [RelayCommand]
    private async Task ReplaceAll()
    {
        if (string.IsNullOrWhiteSpace(Query) || _main.WorkspacePath is not { } workspacePath || Results.Count == 0)
            return;

        Regex pattern;
        try
        {
            pattern = BuildReplacePattern(Query, MatchCase, WholeWord, UseRegex);
        }
        catch (ArgumentException ex)
        {
            _main.StatusMessage = $"Replace: invalid regular expression ({ex.Message})";
            return;
        }

        var fileGroups = Results.GroupBy(r => r.FilePath).ToList();
        var matchCount = Results.Count;

        if (ConfirmReplaceAll is not null && !await ConfirmReplaceAll(matchCount, fileGroups.Count))
            return;

        IsSearching = true;
        var replacedFiles = 0;

        try
        {
            foreach (var fileGroup in fileGroups)
            {
                try
                {
                    var content = await _fileSystemService.ReadAllTextAsync(fileGroup.Key);

                    // Regex mode keeps $ substitution semantics; literal modes use a
                    // match evaluator so the replacement text is always verbatim.
                    var updated = UseRegex
                        ? pattern.Replace(content, ReplaceText)
                        : pattern.Replace(content, _ => ReplaceText);

                    if (!string.Equals(content, updated, StringComparison.Ordinal))
                    {
                        await File.WriteAllTextAsync(fileGroup.Key, updated);
                        replacedFiles++;
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Replace-in-files failed for {Path}", fileGroup.Key);
                }
            }

            _main.StatusMessage = $"Replace: {matchCount} occurrence(s) replaced in {replacedFiles} file(s)";
            await SearchFiles();
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    /// Builds the regex used for bulk replacement: the raw pattern in regex mode,
    /// otherwise an escaped literal, optionally \b-anchored for whole-word.
    /// </summary>
    internal static Regex BuildReplacePattern(string query, bool matchCase, bool wholeWord, bool useRegex)
    {
        var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        var timeout = TimeSpan.FromMilliseconds(500);

        if (useRegex)
        {
            return new Regex(query, options, timeout);
        }

        var literal = Regex.Escape(query);
        if (wholeWord)
        {
            literal = @"\b" + literal + @"\b";
        }
        return new Regex(literal, options, timeout);
    }

    /// <summary>
    /// Builds the line matcher for a search. Regex mode takes the query as a pattern
    /// (with a timeout guard against catastrophic backtracking); whole-word wraps the
    /// literal query in \b anchors; otherwise it's a plain substring match.
    /// </summary>
    internal static Func<string, bool> BuildMatcher(string query, bool matchCase, bool wholeWord, bool useRegex)
    {
        var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        var timeout = TimeSpan.FromMilliseconds(500);

        if (useRegex)
        {
            var regex = new Regex(query, options, timeout);
            return line => regex.IsMatch(line);
        }

        if (wholeWord)
        {
            var regex = new Regex(@"\b" + Regex.Escape(query) + @"\b", options, timeout);
            return line => regex.IsMatch(line);
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return line => line.Contains(query, comparison);
    }

    private static List<string> EnumerateFilesSafe(string rootPath)
    {
        var results = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(rootPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
            }))
            {
                results.Add(file);
            }
        }
        catch (Exception ex)
        {
            // Return whatever we collected
            Serilog.Log.Debug(ex, "File enumeration stopped early under {Root}", rootPath);
        }
        return results;
    }

    internal static bool IsInExcludedDirectory(string filePath)
    {
        var parts = filePath.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return parts.Any(p => ExcludedDirectories.Contains(p));
    }

    internal static bool IsBinaryExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".dll" or ".pdb" or ".obj" or ".o" or ".a"
            or ".so" or ".dylib" or ".lib" or ".bin" or ".class" or ".pyc"
            or ".pyo" or ".wasm" or ".node"
            or ".zip" or ".gz" or ".tar" or ".7z" or ".rar" or ".nupkg"
            or ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico"
            or ".svg" or ".webp" or ".tiff" or ".tif"
            or ".mp3" or ".mp4" or ".avi" or ".mov" or ".wav" or ".flac"
            or ".ogg" or ".webm" or ".mkv"
            or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx"
            or ".snk" or ".pfx" or ".p12"
            or ".woff" or ".woff2" or ".ttf" or ".eot" or ".otf"
            or ".sqlite" or ".db" or ".mdb"
            or ".suo" or ".user";
    }
}

public class FileSearchResult
{
    public string FilePath { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public int LineNumber { get; init; }
    public string LineText { get; init; } = "";
    public string Display => $"{RelativePath}:{LineNumber}";
}
