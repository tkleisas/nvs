using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace NVS.Behaviors;

/// <summary>
/// Highlights matching bracket pairs at the caret position.
/// Draws a background rectangle behind both the opening and closing bracket.
/// </summary>
public sealed class BracketHighlightRenderer : IBackgroundRenderer
{
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(80, 0, 122, 204));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 122, 204)), 1);

    private BracketPair? _currentPair;
    private TextDocument? _document;

    public KnownLayer Layer => KnownLayer.Caret;

    public void SetDocument(TextDocument? document) => _document = document;

    public void UpdateBracketPair(BracketPair? pair) => _currentPair = pair;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_document is null || _currentPair is null)
            return;

        DrawBracketHighlight(textView, drawingContext, _currentPair.OpenOffset);
        DrawBracketHighlight(textView, drawingContext, _currentPair.CloseOffset);
    }

    private void DrawBracketHighlight(TextView textView, DrawingContext drawingContext, int offset)
    {
        if (_document is null || offset < 0 || offset >= _document.TextLength)
            return;

        var segment = new TextSegment { StartOffset = offset, Length = 1 };
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            drawingContext.DrawRectangle(HighlightBrush, BorderPen,
                new Rect(rect.X, rect.Y, rect.Width, rect.Height), 2, 2);
        }
    }
}
