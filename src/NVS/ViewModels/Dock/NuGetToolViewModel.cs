using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;
using NVS.Core.Models.NuGet;

namespace NVS.ViewModels.Dock;

public sealed partial class NuGetToolViewModel : Tool
{
    private string _searchQuery = string.Empty;
    private string _selectedTab = "Browse";
    private NuGetPackageInfo? _selectedSearchResult;
    private InstalledPackage? _selectedInstalledPackage;
    private bool _isLoading;
    private bool _includePrerelease;
    private string _statusText = "Ready";
    private string? _selectedProjectPath;

    public MainViewModel Main { get; }

    public ObservableCollection<NuGetPackageInfo> SearchResults { get; } = [];
    public ObservableCollection<InstalledPackage> InstalledPackages { get; } = [];
    public ObservableCollection<InstalledPackage> OutdatedPackages { get; } = [];
    public ObservableCollection<string> ProjectPaths { get; } = [];

    public string SearchQuery
    {
        get => _searchQuery;
        set { if (_searchQuery != value) { _searchQuery = value; OnPropertyChanged(); } }
    }

    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab != value)
            {
                _selectedTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsBrowseTab));
                OnPropertyChanged(nameof(IsInstalledTab));
                OnPropertyChanged(nameof(IsUpdatesTab));
            }
        }
    }

    public bool IsBrowseTab => SelectedTab == "Browse";
    public bool IsInstalledTab => SelectedTab == "Installed";
    public bool IsUpdatesTab => SelectedTab == "Updates";

    public NuGetPackageInfo? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set { if (_selectedSearchResult != value) { _selectedSearchResult = value; OnPropertyChanged(); InstallPackageCommand.NotifyCanExecuteChanged(); } }
    }

    public InstalledPackage? SelectedInstalledPackage
    {
        get => _selectedInstalledPackage;
        set { if (_selectedInstalledPackage != value) { _selectedInstalledPackage = value; OnPropertyChanged(); UninstallPackageCommand.NotifyCanExecuteChanged(); UpdatePackageCommand.NotifyCanExecuteChanged(); } }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { if (_isLoading != value) { _isLoading = value; OnPropertyChanged(); } }
    }

    public bool IncludePrerelease
    {
        get => _includePrerelease;
        set { if (_includePrerelease != value) { _includePrerelease = value; OnPropertyChanged(); } }
    }

    public string StatusText
    {
        get => _statusText;
        private set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    public string? SelectedProjectPath
    {
        get => _selectedProjectPath;
        set
        {
            if (_selectedProjectPath != value)
            {
                _selectedProjectPath = value;
                OnPropertyChanged();
            }
        }
    }

    public NuGetToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "NuGet";
        Title = "📦 NuGet";
        CanClose = true;
        CanPin = true;
    }

    /// <summary>Refresh project list from loaded solution.</summary>
    public void RefreshProjects()
    {
        ProjectPaths.Clear();
        var solution = Main.SolutionService.CurrentSolution;
        if (solution?.Projects is not null)
        {
            var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? "";
            foreach (var project in solution.Projects)
            {
                var fullPath = Path.GetFullPath(Path.Combine(solutionDir, project.RelativePath));
                if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    ProjectPaths.Add(fullPath);
            }
        }

        if (ProjectPaths.Count > 0 && SelectedProjectPath is null)
            SelectedProjectPath = ProjectPaths[0];
    }

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        var service = GetNuGetService();
        if (service is null) { StatusText = "NuGet service unavailable"; return; }

        IsLoading = true;
        StatusText = "Searching...";

        try
        {
            var results = await service.SearchPackagesAsync(SearchQuery, includePrerelease: IncludePrerelease);
            SearchResults.Clear();
            foreach (var pkg in results)
                SearchResults.Add(pkg);
            StatusText = $"Found {results.Count} package(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadInstalled()
    {
        if (SelectedProjectPath is null) { StatusText = "Select a project first"; return; }

        var service = GetNuGetService();
        if (service is null) return;

        IsLoading = true;
        StatusText = "Loading packages...";

        try
        {
            var packages = await service.GetInstalledPackagesAsync(SelectedProjectPath);
            InstalledPackages.Clear();
            foreach (var pkg in packages)
                InstalledPackages.Add(pkg);
            StatusText = $"{packages.Count} installed package(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadOutdated()
    {
        if (SelectedProjectPath is null) { StatusText = "Select a project first"; return; }

        var service = GetNuGetService();
        if (service is null) return;

        IsLoading = true;
        StatusText = "Checking for updates...";

        try
        {
            var packages = await service.GetOutdatedPackagesAsync(SelectedProjectPath);
            OutdatedPackages.Clear();
            foreach (var pkg in packages)
                OutdatedPackages.Add(pkg);
            StatusText = $"{packages.Count} update(s) available";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallPackage()
    {
        if (SelectedProjectPath is null || SelectedSearchResult is null) return;

        var service = GetNuGetService();
        if (service is null) return;

        IsLoading = true;
        StatusText = $"Installing {SelectedSearchResult.Id}...";

        try
        {
            var result = await service.AddPackageAsync(SelectedProjectPath, SelectedSearchResult.Id, SelectedSearchResult.Version);
            StatusText = result.Success
                ? $"Installed {result.PackageId} {result.Version}"
                : $"Failed: {result.ErrorOutput}";

            if (result.Success)
                await LoadInstalled();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanInstall() => SelectedSearchResult is not null && SelectedProjectPath is not null;

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallPackage()
    {
        if (SelectedProjectPath is null || SelectedInstalledPackage is null) return;

        var service = GetNuGetService();
        if (service is null) return;

        IsLoading = true;
        StatusText = $"Removing {SelectedInstalledPackage.Id}...";

        try
        {
            var result = await service.RemovePackageAsync(SelectedProjectPath, SelectedInstalledPackage.Id);
            StatusText = result.Success
                ? $"Removed {result.PackageId}"
                : $"Failed: {result.ErrorOutput}";

            if (result.Success)
                await LoadInstalled();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanUninstall() => SelectedInstalledPackage is not null && SelectedProjectPath is not null;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdatePackage()
    {
        if (SelectedProjectPath is null || SelectedInstalledPackage is null) return;

        var service = GetNuGetService();
        if (service is null) return;

        IsLoading = true;
        StatusText = $"Updating {SelectedInstalledPackage.Id}...";

        try
        {
            var result = await service.UpdatePackageAsync(
                SelectedProjectPath,
                SelectedInstalledPackage.Id,
                SelectedInstalledPackage.LatestVersion);
            StatusText = result.Success
                ? $"Updated {result.PackageId} to {result.Version}"
                : $"Failed: {result.ErrorOutput}";

            if (result.Success)
                await LoadOutdated();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanUpdate() => SelectedInstalledPackage?.HasUpdate == true && SelectedProjectPath is not null;

    [RelayCommand]
    private async Task Restore()
    {
        var path = SelectedProjectPath ?? Main.SolutionService.CurrentSolution?.FilePath;
        if (path is null) { StatusText = "No project or solution loaded"; return; }

        var service = GetNuGetService();
        if (service is null) return;

        IsLoading = true;
        StatusText = "Restoring...";

        try
        {
            var result = await service.RestoreAsync(path);
            StatusText = result.Success ? "Restore completed" : $"Restore failed: {result.ErrorOutput}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        SelectedTab = tab;
    }

    private INuGetService? GetNuGetService()
    {
        try
        {
            return App.Current?.Services?.GetService(typeof(INuGetService)) as INuGetService;
        }
        catch
        {
            return null;
        }
    }

    private new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
    }
}
