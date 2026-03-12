using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace NVS.Behaviors;

/// <summary>
/// Custom left margin for AvaloniaEdit that renders breakpoint indicators.
/// Clicking toggles breakpoints (red circles) on the clicked line.
/// </summary>
public sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 20;
    private const double CircleRadius = 5.5;

    private static readonly IBrush EnabledBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x17, 0x00));
    private static readonly IBrush VerifiedBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x17, 0x00));
    private static readonly IBrush UnverifiedBrush = new SolidColorBrush(Color.FromRgb(0x84, 0x84, 0x84));
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xE5, 0x17, 0x00));

    private HashSet<int> _breakpointLines = [];
    private HashSet<int> _verifiedLines = [];
    private int _hoverLine = -1;

    /// <summary>
    /// Called when the user clicks to toggle a breakpoint.
    /// Parameter is the 1-based line number.
    /// </summary>
    public event EventHandler<int>? BreakpointToggled;

    public void UpdateBreakpoints(IEnumerable<(int Line, bool Verified)> breakpoints)
    {
        _breakpointLines = [];
        _verifiedLines = [];

        foreach (var (line, verified) in breakpoints)
        {
            _breakpointLines.Add(line);
            if (verified) _verifiedLines.Add(line);
        }

        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(MarginWidth, 0);
    }

    public override void Render(DrawingContext drawingContext)
    {
        if (TextView is null || !TextView.VisualLinesValid)
            return;

        // Dark background for gutter
        drawingContext.FillRectangle(
            new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            new Rect(0, 0, Bounds.Width, Bounds.Height));

        foreach (var visualLine in TextView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            var y = visualLine.GetTextLineVisualYPosition(
                visualLine.TextLines[0], VisualYPosition.TextMiddle) - TextView.VerticalOffset;

            if (_breakpointLines.Contains(lineNumber))
            {
                var brush = _verifiedLines.Contains(lineNumber) ? VerifiedBrush : UnverifiedBrush;
                drawingContext.DrawEllipse(
                    brush, null,
                    new Point(MarginWidth / 2, y),
                    CircleRadius, CircleRadius);
            }
            else if (lineNumber == _hoverLine)
            {
                drawingContext.DrawEllipse(
                    HoverBrush, null,
                    new Point(MarginWidth / 2, y),
                    CircleRadius, CircleRadius);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var line = GetLineFromPoint(e.GetPosition(this));
        if (line != _hoverLine)
        {
            _hoverLine = line;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverLine = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var line = GetLineFromPoint(e.GetPosition(this));
        if (line > 0)
        {
            BreakpointToggled?.Invoke(this, line);
            e.Handled = true;
        }
    }

    private int GetLineFromPoint(Point point)
    {
        if (TextView is null) return -1;

        var visualTop = point.Y + TextView.VerticalOffset;
        var visualLine = TextView.GetVisualLineFromVisualTop(visualTop);
        return visualLine?.FirstDocumentLine.LineNumber ?? -1;
    }
}
