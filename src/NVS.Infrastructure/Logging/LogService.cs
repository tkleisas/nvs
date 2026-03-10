using NVS.Core.Enums;
using NVS.Core.Interfaces;
using Serilog;

namespace NVS.Infrastructure.Logging;

public sealed class LogService : ILogService
{
    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        switch (level)
        {
            case LogLevel.Trace:
                Serilog.Log.Verbose(exception, message);
                break;
            case LogLevel.Debug:
                Serilog.Log.Debug(exception, message);
                break;
            case LogLevel.Information:
                Serilog.Log.Information(exception, message);
                break;
            case LogLevel.Warning:
                Serilog.Log.Warning(exception, message);
                break;
            case LogLevel.Error:
                Serilog.Log.Error(exception, message);
                break;
            case LogLevel.Fatal:
                Serilog.Log.Fatal(exception, message);
                break;
            default:
                Serilog.Log.Information(exception, message);
                break;
        }
    }
}
