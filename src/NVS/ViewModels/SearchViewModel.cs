using System.Collections.ObjectModel;
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
                        if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
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
