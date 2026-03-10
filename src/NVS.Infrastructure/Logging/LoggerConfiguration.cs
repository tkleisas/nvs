using Serilog;
using Serilog.Events;

namespace NVS.Infrastructure.Logging;

public static class LoggerConfiguration
{
    public static ILogger CreateLogger(string logDirectory)
    {
        var logPath = Path.Combine(logDirectory, "nvs-.log");
        
        return new Serilog.LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
    
    public static void ConfigureGlobalLogger(string logDirectory)
    {
        Log.Logger = CreateLogger(logDirectory);
    }
}
