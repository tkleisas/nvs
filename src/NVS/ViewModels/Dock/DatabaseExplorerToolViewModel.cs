using Dock.Model.Mvvm.Controls;
using NVS.Helpers;
using SQLiteExplorer.Lib.ViewModels;

namespace NVS.ViewModels.Dock;

public class DatabaseExplorerToolViewModel : Document
{
    public MainViewModel Main { get; }
    public MainWindowViewModel DatabaseViewModel { get; }

    public DatabaseExplorerToolViewModel(MainViewModel main)
    {
        Main = main;
        Id = "DatabaseExplorer";
        Title = "🗄 Database";
        CanClose = true;
        CanPin = false;

        DatabaseViewModel = new MainWindowViewModel
        {
            IsMenuVisible = false, // NVS surfaces the explorer commands in its own menus
        };
        WireLlmIntegration();
        UpdateReportStoreDirectory();

        Main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.WorkspacePath) or nameof(MainViewModel.IsWorkspaceOpen))
            {
                UpdateReportStoreDirectory();
            }
        };
    }

    /// <summary>
    /// Routes the explorer's AI features through the NVS LLM service so they use the
    /// endpoint/model configured in NVS Settings. The ⚙ settings button in the explorer
    /// opens the NVS Settings window instead of a separate dialog.
    /// </summary>
    private void WireLlmIntegration()
    {
        try
        {
            var llm = App.Current?.Services?.GetService(typeof(NVS.Core.Interfaces.ILlmService)) as NVS.Core.Interfaces.ILlmService;
            if (llm is null) return;

            DatabaseViewModel.LlmService = new SqlExplorerLlmAdapter(llm);
            DatabaseViewModel.LlmSettingsRequested += (_, _) => Main.RequestOpenSettings();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "LLM integration for Database Explorer unavailable");
        }
    }

    /// <summary>Stores report definitions per workspace when one is open (else AppData).</summary>
    private void UpdateReportStoreDirectory()
    {
        DatabaseViewModel.ReportStoreDirectory = Main.IsWorkspaceOpen && Main.WorkspacePath is not null
            ? Path.Combine(Main.WorkspacePath, ".nvs")
            : null;
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
