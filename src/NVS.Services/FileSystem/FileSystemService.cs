 using NVS.Core.Interfaces;

namespace NVS.Services.FileSystem;

public sealed class FileSystemService : IFileSystemService
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(path));

    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(Directory.Exists(path));

    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => await File.ReadAllTextAsync(path, cancellationToken);

    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        => await File.WriteAllTextAsync(path, content, cancellationToken);

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => await File.ReadAllBytesAsync(path, cancellationToken);

    public async Task WriteAllBytesAsync(string path, byte[] content, CancellationToken cancellationToken = default)
        => await File.WriteAllBytesAsync(path, content, cancellationToken);

    public Task<IReadOnlyList<string>> GetFilesAsync(string directory, string searchPattern, bool recursive = true, CancellationToken cancellationToken = default)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directory, searchPattern, searchOption);
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory, CancellationToken cancellationToken = default)
    {
        var dirs = Directory.GetDirectories(directory);
        return Task.FromResult<IReadOnlyList<string>>(dirs);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeleteDirectoryAsync(string path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        Directory.Delete(path, recursive);
        return Task.CompletedTask;
    }

    public Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        File.Copy(sourcePath, destinationPath, overwrite);
        return Task.CompletedTask;
    }

    public Task MoveFileAsync(string sourcePath, string destinationPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        if (overwrite && File.Exists(destinationPath))
            File.Delete(destinationPath);
        File.Move(sourcePath, destinationPath);
        return Task.CompletedTask;
    }

    public string GetTempPath() => Path.GetTempPath();
    public string GetAppDataPath() => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    
    public string GetUserDataPath()
    {
        var appData = GetAppDataPath();
        var nvsPath = System.IO.Path.Combine(appData, "NVS");
        Directory.CreateDirectory(nvsPath);
        return nvsPath;
    }

    public IFileSystemWatcher CreateWatcher(string path, string filter = "*")
    {
        return new FileSystemWatcherWrapper(path, filter);
    }

    private sealed class FileSystemWatcherWrapper : IFileSystemWatcher
    {
        private readonly System.IO.FileSystemWatcher _watcher;
        private bool _enableRaisingEvents;

        public FileSystemWatcherWrapper(string path, string filter)
        {
            Path = path;
            Filter = filter;
            _watcher = new System.IO.FileSystemWatcher(path, filter)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = false
            };

            _watcher.Created += (_, e) => OnCreated(e);
            _watcher.Deleted += (_, e) => OnDeleted(e);
            _watcher.Changed += (_, e) => OnChanged(e);
            _watcher.Renamed += (_, e) => OnRenamed(e);
        }

        private void OnCreated(System.IO.FileSystemEventArgs e)
        {
            Created?.Invoke(this, new NVS.Core.Interfaces.FileSystemEventArgs
            {
                FullPath = e.FullPath,
                Name = e.Name ?? string.Empty,
                ChangeType = ConvertChangeType(e.ChangeType)
            });
        }

        private void OnDeleted(System.IO.FileSystemEventArgs e)
        {
            Deleted?.Invoke(this, new NVS.Core.Interfaces.FileSystemEventArgs
            {
                FullPath = e.FullPath,
                Name = e.Name ?? string.Empty,
                ChangeType = ConvertChangeType(e.ChangeType)
            });
        }

        private void OnChanged(System.IO.FileSystemEventArgs e)
        {
            Changed?.Invoke(this, new NVS.Core.Interfaces.FileSystemEventArgs
            {
                FullPath = e.FullPath,
                Name = e.Name ?? string.Empty,
                ChangeType = ConvertChangeType(e.ChangeType)
            });
        }

        private void OnRenamed(System.IO.RenamedEventArgs e)
        {
            Renamed?.Invoke(this, new NVS.Core.Interfaces.RenamedEventArgs
            {
                FullPath = e.FullPath,
                Name = e.Name ?? string.Empty,
                OldFullPath = e.OldFullPath,
                OldName = e.OldName ?? string.Empty,
                ChangeType = ConvertChangeType(e.ChangeType)
            });
        }

        private static NVS.Core.Interfaces.WatcherChangeTypes ConvertChangeType(System.IO.WatcherChangeTypes type)
        {
            return type switch
            {
                System.IO.WatcherChangeTypes.Created => NVS.Core.Interfaces.WatcherChangeTypes.Created,
                System.IO.WatcherChangeTypes.Deleted => NVS.Core.Interfaces.WatcherChangeTypes.Deleted,
                System.IO.WatcherChangeTypes.Changed => NVS.Core.Interfaces.WatcherChangeTypes.Changed,
                System.IO.WatcherChangeTypes.Renamed => NVS.Core.Interfaces.WatcherChangeTypes.Renamed,
                _ => NVS.Core.Interfaces.WatcherChangeTypes.Created
            };
        }

        public bool EnableRaisingEvents
        {
            get => _enableRaisingEvents;
            set
            {
                _enableRaisingEvents = value;
                _watcher.EnableRaisingEvents = value;
            }
        }

        public string Path { get; }
        public string Filter { get; }
        
        public bool IncludeSubdirectories
        {
            get => _watcher.IncludeSubdirectories;
            set => _watcher.IncludeSubdirectories = value;
        }

        public event EventHandler<NVS.Core.Interfaces.FileSystemEventArgs>? Created;
        public event EventHandler<NVS.Core.Interfaces.FileSystemEventArgs>? Deleted;
        public event EventHandler<NVS.Core.Interfaces.FileSystemEventArgs>? Changed;
        public event EventHandler<NVS.Core.Interfaces.RenamedEventArgs>? Renamed;

        public void Dispose() => _watcher.Dispose();
    }
}
