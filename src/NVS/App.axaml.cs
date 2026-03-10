using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NVS.Infrastructure.DependencyInjection;
using NVS.ViewModels;
using Serilog;

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
            var mainWindow = Services?.GetService(typeof(MainWindow)) as MainWindow ?? new MainWindow();
            mainWindow.DataContext = Services?.GetService(typeof(MainViewModel)) as MainViewModel;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddNvsInfrastructure();
        services.AddNvsServices();

        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logPath = Path.Combine(appDataPath, "NVS", "logs");
        Directory.CreateDirectory(logPath);
        
        Infrastructure.Logging.LoggerConfiguration.ConfigureGlobalLogger(logPath);
        Log.Information("NVS starting up...");
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNvsServices(this IServiceCollection services)
    {
        return services;
    }
}
