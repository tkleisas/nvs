using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NVS.Core.Interfaces;
using NVS.Infrastructure.DependencyInjection;
using NVS.Services.Editor;
using NVS.Services.FileSystem;
using NVS.Services.Git;
using NVS.Services.Languages;
using NVS.Services.Lsp;
using NVS.Services.Settings;
using NVS.Services.Terminal;
using NVS.Services.Workspaces;
using NVS.ViewModels;

namespace NVS;

public partial class App : Application
{
    public new static App? Current => (App?)Application.Current;
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Load persisted settings before creating the window
            var settingsService = Services?.GetService(typeof(ISettingsService)) as ISettingsService;
            if (settingsService is not null)
            {
                try { Task.Run(() => settingsService.InitializeAsync()).GetAwaiter().GetResult(); }
                catch { /* Continue with defaults if settings fail to load */ }
            }

            var mainWindow = Services?.GetService(typeof(MainWindow)) as MainWindow ?? new MainWindow();

            // Restore window size/position/state from settings
            var windowSettings = settingsService?.AppSettings.Window ?? new NVS.Core.Models.Settings.WindowSettings();
            mainWindow.ApplyWindowSettings(windowSettings);

            var mainViewModel = Services?.GetService(typeof(MainViewModel)) as MainViewModel;
            if (mainViewModel != null)
            {
                mainViewModel.StorageProvider = mainWindow.StorageProvider;
                mainViewModel.InitializeDock();
            }
            mainWindow.DataContext = mainViewModel;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddNvsInfrastructure();
        
        // Core services
        services.AddSingleton<IEditorService, EditorService>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILanguageService, LanguageService>();
        services.AddSingleton<ILspClientFactory, LspClientFactory>();
        services.AddSingleton<ILspSessionManager, LspSessionManager>();
        services.AddSingleton<ILanguageServerManager, LanguageServerManager>();
        services.AddSingleton<IGitService, GitService>();
        services.AddSingleton<ITerminalService, TerminalService>();
        
        // ViewModels
        services.AddSingleton<EditorViewModel>();
        services.AddTransient<MainViewModel>();
        
        // Views
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logPath = Path.Combine(appDataPath, "NVS", "logs");
        Directory.CreateDirectory(logPath);
        
        Infrastructure.Logging.LoggerConfiguration.ConfigureGlobalLogger(logPath);
        Serilog.Log.Information("NVS starting up...");
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNvsServices(this IServiceCollection services)
    {
        return services;
    }
}
