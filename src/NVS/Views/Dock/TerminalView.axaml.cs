using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using NVS.Core.Interfaces;
using NVS.ViewModels.Dock;
using Serilog;

namespace NVS.Views.Dock;

public partial class TerminalView : UserControl
{
    private bool _userScrolledUp;

    public TerminalView()
    {
        InitializeComponent();
        Focusable = true;
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    // ── Keyboard ───────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is TerminalToolViewModel tool && tool.Terminal is { IsRunning: true } t)
        {
            var text = MapKey(e);
            if (text is not null)
            {
                _ = t.SendInputAsync(text);
                e.Handled = true;
                return;
            }
        }
        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (DataContext is TerminalToolViewModel tool && tool.Terminal is { IsRunning: true } t
            && !string.IsNullOrEmpty(e.Text))
        {
            _ = t.SendInputAsync(e.Text);
            e.Handled = true;
            return;
        }
        base.OnTextInput(e);
    }

    internal static string? MapKey(KeyEventArgs e)
    {
        var mod = e.KeyModifiers;
        if (mod.HasFlag(KeyModifiers.Control) && !mod.HasFlag(KeyModifiers.Alt))
        {
            return e.Key switch
            {
                Key.C => "\x03",
                Key.D => "\x04",
                Key.Z => "\x1A",
                Key.L => "\x0C",
                _ => null,
            };
        }

        return e.Key switch
        {
            Key.Return => "\r",
            Key.Back => "\x7F",
            Key.Tab => "\t",
            Key.Escape => "\x1B",
            Key.Up => "\x1B[A",
            Key.Down => "\x1B[B",
            Key.Right => "\x1B[C",
            Key.Left => "\x1B[D",
            Key.Home => "\x1B[H",
            Key.End => "\x1B[F",
            Key.Delete => "\x1B[3~",
            Key.PageUp => "\x1B[5~",
            Key.PageDown => "\x1B[6~",
            Key.F1 => "\x1BOP", Key.F2 => "\x1BOQ", Key.F3 => "\x1BOR", Key.F4 => "\x1BOS",
            Key.F5 => "\x1B[15~", Key.F6 => "\x1B[17~", Key.F7 => "\x1B[18~", Key.F8 => "\x1B[19~",
            Key.F9 => "\x1B[20~", Key.F10 => "\x1B[21~", Key.F11 => "\x1B[23~", Key.F12 => "\x1B[24~",
            _ => null,
        };
    }

    // ── Layout / Font / Scroll ─────────────────────────────────────

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplyFontSettings();
        WireScrollTracking();
        Terminal.OutputAppended += OnTerminalOutputAppended;
        if (DataContext is TerminalToolViewModel vm)
            vm.PropertyChanged += OnToolPropertyChanged;
    }

    private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not TerminalToolViewModel vm) return;
        if (e.PropertyName is nameof(TerminalToolViewModel.TerminalFontFamily)
            or nameof(TerminalToolViewModel.TerminalFontSize))
            Dispatcher.UIThread.Post(ApplyFontSettings);
    }

    /// <summary>
    /// Track whether the user has manually scrolled upward to browse scrollback.
    /// Only PointerWheelChanged is a reliable user-initiated signal here;
    /// ScrollChanged fires for programmatic extent changes too.
    /// </summary>
    private void WireScrollTracking()
    {
        TerminalScroller.PointerWheelChanged += (_, e) =>
        {
            if (e.Delta.Y < 0)
                _userScrolledUp = true;
            else if (e.Delta.Y > 0 && IsNearBottom())
                _userScrolledUp = false;
        };
    }

    private void OnTerminalOutputAppended(object? s, EventArgs e)
    {
        if (_userScrolledUp) return;
        TerminalScroller.UpdateLayout();

        var screen = Terminal.VisualScreen;
        if (screen is null) return;

        double viewportH = TerminalScroller.Viewport.Height;
        double cellH = Terminal.CellMeasuredHeight;
        double scrollback = screen.ScrollbackCount;
        double cursorY = 2 + (scrollback + screen.CursorRow + 1) * cellH;
        double maxOffset = Math.Max(0, TerminalScroller.Extent.Height - viewportH);
        double target = Math.Clamp(cursorY - viewportH + cellH, 0, maxOffset);
        TerminalScroller.Offset = new Vector(TerminalScroller.Offset.X, target);
    }

    private bool IsNearBottom()
    {
        if (TerminalScroller.Viewport.Height <= 0) return true;
        return TerminalScroller.Offset.Y + TerminalScroller.Viewport.Height >= TerminalScroller.Extent.Height - 20;
    }

    private void ApplyFontSettings()
    {
        if (Terminal is null || DataContext is not TerminalToolViewModel vm) return;
        if (!string.IsNullOrWhiteSpace(vm.TerminalFontFamily))
            Terminal.FontFamilyName = vm.TerminalFontFamily;
        if (vm.TerminalFontSize > 0)
            Terminal.CellFontSize = vm.TerminalFontSize;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        ApplyFontSettings();
    }
}