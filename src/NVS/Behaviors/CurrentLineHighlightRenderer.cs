using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace NVS.Behaviors;

/// <summary>
/// Renders a yellow highlight on the current debug execution line.
/// Shows a yellow background bar across the full line width when the debugger
/// is paused at a specific source line.
/// </summary>
public sealed class CurrentLineHighlightRenderer : IBackgroundRenderer
{
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xE0, 0x00));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xE0, 0x00)), 1);

    private readonly TextDocument _document;
    private int? _currentLine;

    public KnownLayer Layer => KnownLayer.Selection;

    public CurrentLineHighlightRenderer(TextDocument document)
    {
        _document = document;
    }

    public void SetCurrentLine(int? line)
    {
        _currentLine = line;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_currentLine is not { } line || line < 1 || line > _document.LineCount)
            return;

        var docLine = _document.GetLineByNumber(line);
        var segment = new TextSegment { StartOffset = docLine.Offset, Length = docLine.Length };

        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            // Draw a full-width highlight bar
            var fullRect = new Rect(0, rect.Top, textView.Bounds.Width, rect.Height);
            drawingContext.FillRectangle(HighlightBrush, fullRect);
            drawingContext.DrawRectangle(BorderPen, fullRect);
        }
    }
}
