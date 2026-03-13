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

    /// <summary>
    /// Executes SQL in the Database Explorer. Requires an open database connection.
    /// </summary>
    public async Task ExecuteSql(string sql)
    {
        await DatabaseViewModel.ExecuteSqlAsync(sql);
    }

    /// <summary>
    /// Whether a database is currently connected.
    /// </summary>
    public bool IsConnected => DatabaseViewModel.IsConnected;
}
