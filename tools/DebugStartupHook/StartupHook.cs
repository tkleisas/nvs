using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

/// <summary>
/// .NET startup hook loaded via DOTNET_STARTUP_HOOKS.
/// Writes the process ID to a temp file, then waits for a debugger to attach
/// and for NVS to signal that breakpoints are set, before letting the
/// application continue.
/// </summary>
internal class StartupHook
{
    public static void Initialize()
    {
        var pidFile = Environment.GetEnvironmentVariable("NVS_DEBUG_PID_FILE");
        if (string.IsNullOrEmpty(pidFile))
            return;

        var readyFile = Environment.GetEnvironmentVariable("NVS_DEBUG_READY_FILE");
        var goFile = Environment.GetEnvironmentVariable("NVS_DEBUG_GO_FILE");
        var programDll = Environment.GetEnvironmentVariable("NVS_DEBUG_PROGRAM");

        // Write our PID so NVS can attach the debugger
        File.WriteAllText(pidFile, Process.GetCurrentProcess().Id.ToString());

        // Spin until the debugger attaches (timeout after 30 seconds)
        int waited = 0;
        while (!Debugger.IsAttached && waited < 30_000)
        {
            Thread.Sleep(50);
            waited += 50;
        }

        if (!Debugger.IsAttached)
            return;

        // Phase 1: Wait for NVS to signal that ConfigurationDone is complete.
        if (!string.IsNullOrEmpty(readyFile))
        {
            int readyWaited = 0;
            while (!File.Exists(readyFile) && readyWaited < 10_000)
            {
                Thread.Sleep(50);
                readyWaited += 50;
            }
        }

        // Pre-load the main assembly so netcoredbg can resolve breakpoints
        // while this hook is still holding execution.
        if (!string.IsNullOrEmpty(programDll))
        {
            try
            {
                System.Reflection.Assembly.LoadFrom(programDll);
            }
            catch
            {
                // Best effort — if pre-load fails, breakpoints may not resolve
            }
        }

        // Phase 2: Wait for NVS to signal breakpoints have been re-synced.
        if (!string.IsNullOrEmpty(goFile))
        {
            int goWaited = 0;
            while (!File.Exists(goFile) && goWaited < 10_000)
            {
                Thread.Sleep(50);
                goWaited += 50;
            }
        }
        else
        {
            Thread.Sleep(200);
        }
    }
}
