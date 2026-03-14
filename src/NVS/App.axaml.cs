using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NVS.Core.Interfaces;
using NVS.Infrastructure.DependencyInjection;
using NVS.Services.Build;
using NVS.Services.Debug;
using NVS.Services.Editor;
using NVS.Services.FileSystem;
using NVS.Services.Git;
using NVS.Services.Languages;
using NVS.Services.Lsp;
using NVS.Services.Settings;
using NVS.Services.Solution;
using NVS.Services.Template;
using NVS.Services.Terminal;
using NVS.Services.Workspaces;
using NVS.Services.LLM;
using NVS.Services.LLM.Tools;
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
                RegisterLlmTools(mainViewModel);

                // Restore previous workspace if the setting is enabled
                if (settingsService?.AppSettings is { RestorePreviousSession: true, LastWorkspacePath: { } lastPath })
                {
                    if (Directory.Exists(lastPath))
                    {
                        _ = mainViewModel.OpenWorkspaceAsync(lastPath);
                    }
                    else
                    {
                        // Path no longer exists — clear it so we don't retry every launch
                        mainViewModel.StatusMessage = $"Previous workspace not found: {lastPath}";
                        var cleaned = settingsService.AppSettings with { LastWorkspacePath = null };
                        _ = settingsService.SaveAppSettingsAsync(cleaned);
                    }
                }
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
        services.AddSingleton<ISolutionService, SolutionService>();
        services.AddSingleton<IBuildService, BuildService>();
        services.AddSingleton<IDebugService, DebugService>();
        services.AddSingleton<IBreakpointStore, BreakpointStore>();
        services.AddSingleton<DebugAdapterDownloader>();
        services.AddSingleton<DebugAdapterRegistry>();
        services.AddSingleton<ITemplateService, TemplateService>();
        services.AddSingleton<ILlmService, LlmService>();
        services.AddSingleton<INuGetService, NVS.Services.NuGet.NuGetPackageService>();
        services.AddSingleton<ICodeMetricsService, NVS.Services.Metrics.CodeMetricsService>();
        
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

    private void RegisterLlmTools(MainViewModel mainVm)
    {
        if (Services?.GetService(typeof(ILlmService)) is not LlmService llmService)
            return;

        Func<string?> getWorkspace = () => mainVm.WorkspacePath;
        Func<string> getWorkspaceOrDefault = () => mainVm.WorkspacePath ?? Environment.CurrentDirectory;

        llmService.RegisterTool(new ReadFileTool(getWorkspaceOrDefault));
        llmService.RegisterTool(new WriteFileTool(getWorkspaceOrDefault));
        llmService.RegisterTool(new ListFilesTool(getWorkspaceOrDefault));
        llmService.RegisterTool(new SearchFilesTool(getWorkspaceOrDefault));
        llmService.RegisterTool(new ReadEditorTool(() =>
        {
            var doc = mainVm.Editor?.ActiveDocument;
            if (doc is null) return null;
            return new ReadEditorTool.EditorState
            {
                FilePath = doc.Document.FilePath,
                FileName = doc.Document.Name,
                Language = doc.Language.ToString(),
                Content = doc.Text ?? string.Empty,
                CursorLine = doc.CursorLine,
                CursorColumn = doc.CursorColumn
            };
        }));
        llmService.RegisterTool(new ApplyEditTool(op =>
        {
            var doc = mainVm.Editor?.ActiveDocument;
            if (doc is null) return false;
            // Simple: replace entire content or specific lines
            if (op.ReplaceSelection || (!op.LineStart.HasValue && !op.LineEnd.HasValue))
            {
                doc.Text = op.NewText;
                return true;
            }
            var lines = (doc.Text ?? "").Split('\n').ToList();
            var start = Math.Max(0, (op.LineStart ?? 1) - 1);
            var end = Math.Min(lines.Count, op.LineEnd ?? lines.Count);
            var count = end - start;
            if (count > 0)
                lines.RemoveRange(start, count);
            lines.InsertRange(start, op.NewText.Split('\n'));
            doc.Text = string.Join('\n', lines);
            return true;
        }));

        Serilog.Log.Information("Registered {Count} LLM agent tools", 6);
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNvsServices(this IServiceCollection services)
    {
        return services;
    }
}
