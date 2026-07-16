using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NVS.Core.Interfaces;
using NVS.Core.Terminal;
using Serilog;

namespace NVS.Views.Dock;

/// <summary>
/// Custom terminal view — renders a <see cref="Core.Terminal.TerminalScreen"/> on an
/// Avalonia <see cref="DrawingContext"/> (Skia-backed), driven by an <see cref="IProcessTerminal"/>.
/// Keyboard input forwards to the terminal; a blink timer animates the cursor.
/// </summary>
public sealed class ProcessTerminalView : Control
{
    public static readonly StyledProperty<IProcessTerminal?> TerminalProperty =
        AvaloniaProperty.Register<ProcessTerminalView, IProcessTerminal?>(nameof(Terminal));

    public static readonly StyledProperty<Core.Terminal.TerminalScreen?> VisualScreenProperty =
        AvaloniaProperty.Register<ProcessTerminalView, Core.Terminal.TerminalScreen?>(nameof(VisualScreen));

    public static readonly StyledProperty<string> FontFamilyNameProperty =
        AvaloniaProperty.Register<ProcessTerminalView, string>(nameof(FontFamilyName), "Consolas,Cascadia Mono,Courier New,monospace");

    public static readonly StyledProperty<double> CellFontSizeProperty =
        AvaloniaProperty.Register<ProcessTerminalView, double>(nameof(CellFontSize), 15.0);

    public IProcessTerminal? Terminal
    {
        get => GetValue(TerminalProperty);
        set => SetValue(TerminalProperty, value);
    }

    public Core.Terminal.TerminalScreen? VisualScreen
    {
        get => GetValue(VisualScreenProperty);
        set => SetValue(VisualScreenProperty, value);
    }

    public string FontFamilyName
    {
        get => GetValue(FontFamilyNameProperty);
        set => SetValue(FontFamilyNameProperty, value);
    }

    public double CellFontSize
    {
        get => GetValue(CellFontSizeProperty);
        set => SetValue(CellFontSizeProperty, value);
    }

    private VtParser? _parser;
    private Typeface _typeface = new("Consolas");
    private double _cellWidth = 8.4;
    private double _cellHeight = 18;

    /// <summary>Measured monospace cell height in device pixels — exposed for scroll-offset calculations.</summary>
    public double CellMeasuredHeight => _cellHeight;
    private IDisposable? _outputSub;
    private DispatcherTimer? _blinkTimer;
    private bool _cursorVisible = true;
    private bool _needMeasure = true;
    private bool _resizing;

    /// <summary>Fires after new output has been parsed into the screen buffer. Throttled to ~300ms.</summary>
    public event EventHandler? OutputAppended;
    private DateTime _lastOutputEvent = DateTime.MinValue;
    private bool _outputPending;
    private static readonly TimeSpan OutputThrottle = TimeSpan.FromMilliseconds(300);

    public ProcessTerminalView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Terminal is null && DataContext is ViewModels.Dock.TerminalToolViewModel tool)
            _ = TryCreateShellAsync(tool);
    }

    private async Task TryCreateShellAsync(ViewModels.Dock.TerminalToolViewModel tool)
    {
        var host = tool.Main.TerminalHost;
        if (host is null) return;
        var t = await host.CreateShellAsync(new TerminalStartOptions
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(tool.WorkingDirectory)
                ? tool.Main.WorkspacePath
                : tool.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        });
        tool.Terminal = t;
        Terminal = t;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        switch (change.Property.Name)
        {
            case nameof(Terminal):
                DetachTerminal();
                if (Terminal is not null) AttachTerminal(Terminal);
                break;
            case nameof(FontFamilyName):
            case nameof(CellFontSize):
                _needMeasure = true;
                _typeface = new Typeface(FontFamilyName);
                InvalidateMeasure();
                InvalidateVisual();
                break;
        }
    }

    private void AttachTerminal(IProcessTerminal terminal)
    {
        var screen = terminal.Session.Kind switch
        {
            TerminalSessionKind.Shell => new Core.Terminal.TerminalScreen(120, 30, 10000),
            _ => new Core.Terminal.TerminalScreen(120, 30, 2000),
        };
        VisualScreen = screen;
        _parser = new VtParser(screen);

        _outputSub = terminal.OutputObservable.Subscribe(new AnonymousTerminalObserver(chunk =>
        {
            var raw = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(chunk.Text));
            _parser?.Feed(raw);
            Serilog.Log.Debug("[TermView] chunk {Len}b, queuing invalidate", raw.Length);
            Dispatcher.UIThread.Post(() =>
            {
                InvalidateMeasure();
                InvalidateVisual();
                ThrottledOutputAppended();
            });
        }));

        terminal.Exited += (_, _) => Dispatcher.UIThread.Post(() => { InvalidateMeasure(); InvalidateVisual(); });

        _blinkTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Normal,
            (_, _) => { _cursorVisible = !_cursorVisible; InvalidateVisual(); });
        _blinkTimer.Start();

        InvalidateMeasure();
    }

    private void DetachTerminal()
    {
        _blinkTimer?.Stop();
        _blinkTimer = null;
        _outputSub?.Dispose();
        _outputSub = null;
        _parser = null;
        VisualScreen = null;
    }

    /// <summary>
    /// Raises <see cref="OutputAppended"/> at most once per ~300ms. If output
    /// arrives during the cooldown, a single deferred event fires after the
    /// interval elapses so the last batch is always surfaced.
    /// </summary>
    private void ThrottledOutputAppended()
    {
        var now = DateTime.UtcNow;
        if (now - _lastOutputEvent >= OutputThrottle)
        {
            _lastOutputEvent = now;
            _outputPending = false;
            Serilog.Log.Debug("[TermView] firing OutputAppended (immediate)");
            OutputAppended?.Invoke(this, EventArgs.Empty);
        }
        else if (!_outputPending)
        {
            _outputPending = true;
            var delay = OutputThrottle - (now - _lastOutputEvent);
            Serilog.Log.Debug("[TermView] OutputAppended deferred by {Delay}ms", delay.TotalMilliseconds);
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay);
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_outputPending) return;
                    _outputPending = false;
                    _lastOutputEvent = DateTime.UtcNow;
                    Serilog.Log.Debug("[TermView] firing OutputAppended (deferred)");
                    OutputAppended?.Invoke(this, EventArgs.Empty);
                });
            });
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_needMeasure)
        {
            _needMeasure = false;
            var ft = new FormattedText("W", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                _typeface, CellFontSize, null);
            _cellWidth = Math.Ceiling(ft.Width);
            _cellHeight = Math.Ceiling(ft.Height);
        }
        var screen = VisualScreen;
        if (screen is null) return base.MeasureOverride(availableSize);

        // Recalculate columns when the control's available width changes (dock resize).
        // Inside a ScrollViewer, availableSize.Width is the viewport width.
        if (!_resizing && !double.IsInfinity(availableSize.Width) && availableSize.Width > 40)
        {
            int newCols = Math.Max(1, (int)((availableSize.Width - 4) / _cellWidth));
            if (newCols != screen.Columns)
            {
                _resizing = true;
                screen.Resize(newCols, screen.Rows);
                _ = Terminal?.ResizeAsync(newCols, screen.Rows);
                _resizing = false;
                OutputAppended?.Invoke(this, EventArgs.Empty);
            }
        }

        int cols = screen.Columns, rows = screen.Rows;
        return new Size(cols * _cellWidth + 4, (screen.ScrollbackCount + rows) * _cellHeight + 4);
    }

    public override void Render(DrawingContext context)
    {
        var screen = VisualScreen;
        if (screen is null) return;

        var snap = screen.Snapshot();
        int rows = screen.Rows;
        int cols = screen.Columns;
        double pad = 2;
        double y = pad;

        var dimFgBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180));
        var dimBgBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40));
        var normalFg = new SolidColorBrush(Color.FromRgb(212, 212, 212));

        // Render scrollback lines (dimmed, oldest first)
        var scrollback = screen.ScrollbackSnapshot(int.MaxValue);
        foreach (var scrollRow in scrollback)
        {
            RenderRow(context, new Span<Cell>(scrollRow), cols, dimFgBrush, dimBgBrush, pad, y, false);
            y += _cellHeight;
        }

        // Render visible grid
        for (int r = 0; r < rows; r++)
        {
            var rowSpan = new Span<Cell>(snap, r * cols, cols);
            RenderRow(context, rowSpan, cols, normalFg,
                new SolidColorBrush(Colors.Transparent), pad, y, false);
            y += _cellHeight;
        }

        // Cursor (only on visible grid portion)
        if (_cursorVisible && screen.CursorRow >= 0 && screen.CursorRow < rows)
        {
            double cursorY = pad + (scrollback.Count + screen.CursorRow) * _cellHeight;
            double cursorX = pad + screen.CursorCol * _cellWidth;
            context.DrawRectangle(new SolidColorBrush(Colors.White, 0.30), null,
                new Rect(cursorX, cursorY, _cellWidth, _cellHeight));
        }
    }

    private void RenderRow(DrawingContext ctx, Span<Cell> row, int cols,
        IBrush defaultFg, IBrush defaultBg, double x, double y, bool dim)
    {
        for (int c = 0; c < cols; c++)
        {
            var cell = row[c];
            if (cell.Char == ' ' || cell.Char == 0) continue;

            double cx = x + c * _cellWidth;
            var bg = ResolveBrush(cell, dim);
            if (bg is not null)
                ctx.DrawRectangle(bg, null, new Rect(cx, y, _cellWidth, _cellHeight));

            var fg = ResolveFgBrush(cell, defaultFg, dim);
            var tf = new Typeface(_typeface.FontFamily,
                cell.Rendition.HasFlag(RenditionFlags.Italic) ? FontStyle.Italic : FontStyle.Normal,
                cell.Rendition.HasFlag(RenditionFlags.Bold) ? FontWeight.Bold : FontWeight.Normal);
            var ft = new FormattedText(cell.Char.ToString(), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, CellFontSize, fg);
            ctx.DrawText(ft, new Point(cx, y));
        }
    }

    private static IBrush? ResolveBrush(Cell cell, bool dim)
    {
        uint? raw = null;
        if (cell.Background == Cell.DefaultBackground) return null;
        if ((cell.Background & unchecked(Cell.PackedFlag)) != 0)
            raw = (uint)cell.Background & ~unchecked(Cell.PackedFlag);
        else if (cell.Background is >= 0 and < 256)
            raw = Core.Terminal.TerminalPalette.Resolve(cell.Background);
        if (raw is null or 0) return null;
        var color = Color.FromUInt32(raw.Value);
        if (dim) color = new Color((byte)(color.A * 3 / 4), color.R, color.G, color.B);
        return new SolidColorBrush(color);
    }

    private static IBrush ResolveFgBrush(Cell cell, IBrush fallback, bool dim)
    {
        uint? raw = null;
        if ((cell.Foreground & unchecked(Cell.PackedFlag)) != 0)
            raw = (uint)cell.Foreground & ~unchecked(Cell.PackedFlag);
        else if (cell.Foreground is >= 0 and < 256)
            raw = Core.Terminal.TerminalPalette.Resolve(cell.Foreground);
        if (raw is null or 0) return dim ? new SolidColorBrush(Color.FromRgb(150, 150, 150)) : fallback;
        var color = Color.FromUInt32(raw.Value);
        if (cell.Rendition.HasFlag(RenditionFlags.Inverse))
            color = Color.FromUInt32(cell.Background != Cell.DefaultBackground
                ? Core.Terminal.TerminalPalette.Resolve(cell.Background & 255)
                : 0xFFCCCCCC);
        if (dim) color = new Color(color.A, (byte)(color.R * 3 / 4), (byte)(color.G * 3 / 4), (byte)(color.B * 3 / 4));
        return new SolidColorBrush(color);
    }

    private sealed class AnonymousTerminalObserver : IObserver<TerminalOutputChunk>
    {
        private readonly Action<TerminalOutputChunk> _onNext;
        public AnonymousTerminalObserver(Action<TerminalOutputChunk> onNext) => _onNext = onNext;
        public void OnNext(TerminalOutputChunk value) => _onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}