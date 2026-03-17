using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace NVS.Behaviors;

/// <summary>
/// Renders ghost text (dimmed inline completion suggestion) after the cursor.
/// </summary>
public sealed class GhostTextRenderer : IBackgroundRenderer
{
    private readonly TextView _textView;
    private string? _ghostText;
    private int _offset;

    public KnownLayer Layer => KnownLayer.Caret;

    public FontFamily FontFamily { get; set; } = new("Cascadia Code, Consolas, Courier New, monospace");
    public double FontSize { get; set; } = 13;

    public GhostTextRenderer(TextView textView)
    {
        _textView = textView;
    }

    /// <summary>Set or clear the ghost text at the given document offset.</summary>
    public void SetGhostText(string? text, int offset)
    {
        _ghostText = text;
        _offset = offset;
        _textView.InvalidateLayer(Layer);
    }

    /// <summary>Clear the ghost text.</summary>
    public void Clear()
    {
        if (_ghostText is not null)
        {
            _ghostText = null;
            _textView.InvalidateLayer(Layer);
        }
    }

    public bool HasGhostText => _ghostText is not null;
    public string? CurrentGhostText => _ghostText;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_ghostText is null || textView.Document is null)
            return;

        if (_offset < 0 || _offset > textView.Document.TextLength)
            return;

        var location = textView.Document.GetLocation(_offset);
        var visualLine = textView.GetVisualLine(location.Line);
        if (visualLine is null)
            return;

        // Use a zero-length segment at the cursor to find its visual position
        var segment = new TextSegment { StartOffset = _offset, Length = 0 };
        var rects = BackgroundGeometryBuilder.GetRectsForSegment(textView, segment);

        double xPixel;
        double yPixel;

        if (rects.Any())
        {
            var rect = rects.First();
            xPixel = rect.Right;
            yPixel = rect.Top;
        }
        else
        {
            // Fallback: calculate from visual line
            var textLineOffset = visualLine.FirstDocumentLine.Offset;
            var relativeOffset = _offset - textLineOffset;
            var pos = visualLine.GetVisualPosition(relativeOffset, VisualYPosition.TextTop);
            xPixel = pos.X;
            yPixel = visualLine.VisualTop - textView.ScrollOffset.Y;
        }

        var lines = _ghostText.Split('\n');
        var firstLine = lines[0];

        if (string.IsNullOrEmpty(firstLine))
            return;

        var typeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Normal);
        var ghostBrush = new SolidColorBrush(Color.FromArgb(100, 150, 150, 150));
        var lineHeight = visualLine.TextLines.FirstOrDefault()?.Height ?? FontSize * 1.3;

        var formattedText = new FormattedText(
            firstLine,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            ghostBrush);

        drawingContext.DrawText(formattedText, new Point(xPixel, yPixel));

        // Render subsequent lines below
        for (int i = 1; i < lines.Length && i < 4; i++)
        {
            var nextLine = lines[i];
            if (string.IsNullOrEmpty(nextLine)) continue;

            var nextFormattedText = new FormattedText(
                nextLine,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                ghostBrush);

            var nextY = yPixel + (lineHeight * i);
            drawingContext.DrawText(nextFormattedText, new Point(textView.WideSpaceWidth, nextY));
        }
    }
}
