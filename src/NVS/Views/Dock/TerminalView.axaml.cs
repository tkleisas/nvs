using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class TerminalView : UserControl
{
    private DispatcherTimer? _ptyReadyTimer;

    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyFontSettings();
        WireSendCommandDelegate();

        if (DataContext is TerminalToolViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(TerminalToolViewModel.TerminalFontFamily)
                    or nameof(TerminalToolViewModel.TerminalFontSize))
                {
                    Dispatcher.UIThread.Post(ApplyFontSettings);
                }
            };
        }

        // Start a timer to flush pending commands once the PTY is ready.
        // The Iciclecreek TerminalControl launches its PTY asynchronously
        // after template application, so we poll briefly.
        _ptyReadyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _ptyReadyTimer.Tick += OnPtyReadyTimerTick;
        _ptyReadyTimer.Start();
    }

    private int _ptyReadyAttempts;

    private async void OnPtyReadyTimerTick(object? sender, EventArgs e)
    {
        _ptyReadyAttempts++;

        if (DataContext is TerminalToolViewModel vm && TryGetPtyWriter(out _))
        {
            // PTY is ready — wire delegate and flush pending commands
            WireSendCommandDelegate();
            await vm.FlushPendingCommandsAsync();
            _ptyReadyTimer?.Stop();
            _ptyReadyTimer = null;
        }
        else if (_ptyReadyAttempts > 20) // 10 seconds max
        {
            _ptyReadyTimer?.Stop();
            _ptyReadyTimer = null;
        }
    }

    private void ApplyFontSettings()
    {
        if (Terminal == null || DataContext is not TerminalToolViewModel vm) return;

        if (!string.IsNullOrWhiteSpace(vm.TerminalFontFamily))
            Terminal.FontFamily = new FontFamily(vm.TerminalFontFamily);
        if (vm.TerminalFontSize > 0)
            Terminal.FontSize = vm.TerminalFontSize;
    }

    /// <summary>
    /// Tries to get the PTY writer stream from the inner Iciclecreek TerminalView
    /// via reflection (the API is private).
    /// </summary>
    private bool TryGetPtyWriter(out System.IO.Stream? writerStream)
    {
        writerStream = null;
        try
        {
            var innerView = Terminal?.GetVisualDescendants()
                .OfType<Iciclecreek.Terminal.TerminalView>()
                .FirstOrDefault();

            if (innerView is null) return false;

            // Access private _ptyConnection field
            var field = innerView.GetType().GetField(
                "_ptyConnection",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var ptyConnection = field?.GetValue(innerView);
            if (ptyConnection is null) return false;

            // Get WriterStream property from the IPtyConnection
            var writerProp = ptyConnection.GetType().GetProperty("WriterStream");
            writerStream = writerProp?.GetValue(ptyConnection) as System.IO.Stream;
            return writerStream is not null;
        }
        catch
        {
            return false;
        }
    }

    private void WireSendCommandDelegate()
    {
        if (DataContext is not TerminalToolViewModel vm || Terminal is null) return;

        vm.SendCommandAsync = async command =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    if (TryGetPtyWriter(out var writer) && writer is not null)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(command + "\r");
                        await writer.WriteAsync(bytes);
                        await writer.FlushAsync();
                    }
                }
                catch (Exception)
                {
                    // Terminal may not be ready yet
                }
            });
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TerminalToolViewModel vm && Terminal != null)
        {
            Terminal.Process = vm.ShellPath;
            var workDir = vm.WorkingDirectory;
            if (string.IsNullOrEmpty(workDir))
                workDir = vm.Main.WorkspacePath;

            if (!string.IsNullOrEmpty(workDir))
            {
                if (OperatingSystem.IsWindows())
                    Terminal.Args = ["-NoExit", "-Command", $"Set-Location '{workDir}'"];
                else
                    Terminal.Args = ["-c", $"cd \"{workDir}\" && exec $SHELL"];
            }

            WireSendCommandDelegate();
            ApplyFontSettings();
        }
    }
}
