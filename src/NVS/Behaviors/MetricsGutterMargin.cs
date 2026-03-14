using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NVS.Core.Interfaces;

namespace NVS.Behaviors;

/// <summary>
/// Left margin that shows colored dots next to methods based on cyclomatic complexity.
/// Green (CC ≤ 5), Yellow (CC 6–10), Red (CC > 10).
/// </summary>
public sealed class MetricsGutterMargin : AbstractMargin
{
    private const double MarginWidth = 14;
    private const double DotRadius = 3.5;

    private static readonly IBrush GoodBrush = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA));
    private static readonly IBrush DangerBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0x47, 0x47));
    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

    // Map of line number → cyclomatic complexity for method declarations
    private Dictionary<int, int> _methodComplexities = new();

    public void UpdateMetrics(IReadOnlyList<MethodMetrics> methods)
    {
        _methodComplexities = new Dictionary<int, int>();
        foreach (var m in methods)
        {
            _methodComplexities[m.Line] = m.CyclomaticComplexity;
        }
        InvalidateVisual();
    }

    public void Clear()
    {
        _methodComplexities.Clear();
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

        drawingContext.FillRectangle(BackgroundBrush, new Rect(0, 0, Bounds.Width, Bounds.Height));

        if (_methodComplexities.Count == 0) return;

        foreach (var visualLine in TextView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (!_methodComplexities.TryGetValue(lineNumber, out var complexity))
                continue;

            var y = visualLine.GetTextLineVisualYPosition(
                visualLine.TextLines[0], VisualYPosition.TextMiddle) - TextView.VerticalOffset;

            var brush = complexity switch
            {
                <= 5 => GoodBrush,
                <= 10 => WarningBrush,
                _ => DangerBrush,
            };

            drawingContext.DrawEllipse(brush, null, new Point(MarginWidth / 2, y), DotRadius, DotRadius);
        }
    }
}
