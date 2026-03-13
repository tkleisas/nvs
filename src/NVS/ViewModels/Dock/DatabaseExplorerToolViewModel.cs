using Dock.Model.Mvvm.Controls;
using SQLiteExplorer.Lib.ViewModels;

namespace NVS.ViewModels.Dock;

public class DatabaseExplorerToolViewModel : Tool
{
    public MainViewModel Main { get; }
    public MainWindowViewModel DatabaseViewModel { get; }

    public DatabaseExplorerToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "DatabaseExplorer";
        Title = "🗄 Database";
        CanClose = true;
        CanPin = true;

        DatabaseViewModel = new MainWindowViewModel();
    }

    /// <summary>
    /// Opens a SQLite database file in the explorer.
    /// </summary>
    public async Task OpenDatabase(string filePath)
    {
        await DatabaseViewModel.OpenDatabaseByPathAsync(filePath);
    }
}
