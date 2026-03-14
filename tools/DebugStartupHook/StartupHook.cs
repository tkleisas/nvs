using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

/// <summary>
/// .NET startup hook loaded via DOTNET_STARTUP_HOOKS.
/// Writes the process ID to a temp file, then waits for a debugger to attach
/// before letting the application continue. This prevents the race condition
/// where the app runs past breakpoints before the debugger can attach.
/// </summary>
internal class StartupHook
{
    public static void Initialize()
    {
        var pidFile = Environment.GetEnvironmentVariable("NVS_DEBUG_PID_FILE");
        if (string.IsNullOrEmpty(pidFile))
            return;

        // Write our PID so NVS can attach the debugger
        File.WriteAllText(pidFile, Process.GetCurrentProcess().Id.ToString());

        // Spin until the debugger attaches (timeout after 30 seconds)
        int waited = 0;
        while (!Debugger.IsAttached && waited < 30_000)
        {
            Thread.Sleep(50);
            waited += 50;
        }

        // Brief extra pause for breakpoints to be fully set
        if (Debugger.IsAttached)
            Thread.Sleep(200);
    }
}
