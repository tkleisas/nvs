using Avalonia;
using System;
using System.Threading.Tasks;

namespace NVS;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Serilog.Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Serilog.Log.Fatal(ex, "Unhandled domain exception");
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "Application crashed");
            throw;
        }
        finally
        {
            Serilog.Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
