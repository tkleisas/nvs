using NVS.Core.Enums;

namespace NVS.Core.Interfaces;

public interface ILogService
{
    void Log(LogLevel level, string message, Exception? exception = null);
    void Trace(string message) => Log(LogLevel.Trace, message);
    void Debug(string message) => Log(LogLevel.Debug, message);
    void Information(string message) => Log(LogLevel.Information, message);
    void Warning(string message) => Log(LogLevel.Warning, message);
    void Error(string message, Exception? exception = null) => Log(LogLevel.Error, message, exception);
    void Fatal(string message, Exception? exception = null) => Log(LogLevel.Fatal, message, exception);
}
