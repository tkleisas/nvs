namespace NVS.Core.Interfaces;

public interface IFileSystemService
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default);
    
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    Task WriteAllBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<string>> GetFilesAsync(string directory, string searchPattern, bool recursive = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory, CancellationToken cancellationToken = default);
    
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteDirectoryAsync(string path, bool recursive = false, CancellationToken cancellationToken = default);
    Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite = false, CancellationToken cancellationToken = default);
    Task MoveFileAsync(string sourcePath, string destinationPath, bool overwrite = false, CancellationToken cancellationToken = default);
    
    string GetTempPath();
    string GetAppDataPath();
    string GetUserDataPath();
    
    IFileSystemWatcher CreateWatcher(string path, string filter = "*");
}

public interface IFileSystemWatcher : IDisposable
{
    bool EnableRaisingEvents { get; set; }
    string Path { get; }
    string Filter { get; }
    bool IncludeSubdirectories { get; set; }
    
    event EventHandler<FileSystemEventArgs>? Created;
    event EventHandler<FileSystemEventArgs>? Deleted;
    event EventHandler<FileSystemEventArgs>? Changed;
    event EventHandler<RenamedEventArgs>? Renamed;
}

public class FileSystemEventArgs : EventArgs
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required WatcherChangeTypes ChangeType { get; init; }
}

public class RenamedEventArgs : FileSystemEventArgs
{
    public required string OldFullPath { get; init; }
    public required string OldName { get; init; }
}

public enum WatcherChangeTypes
{
    Created = 1,
    Deleted = 2,
    Changed = 4,
    Renamed = 8
}
