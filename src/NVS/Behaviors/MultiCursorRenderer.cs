using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace NVS.Behaviors;

/// <summary>
/// Renders secondary cursors (carets) and selection highlights for multi-caret editing.
/// Added to TextArea.TextView.BackgroundRenderers.
/// </summary>
public sealed class MultiCursorRenderer : IBackgroundRenderer
{
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 200, 200, 200)), 1.5);
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(60, 0, 122, 204));

    private TextDocument? _document;
    private MultiCursorState? _state;

    public KnownLayer Layer => KnownLayer.Caret;

    public void SetDocument(TextDocument? document) => _document = document;

    public void SetState(MultiCursorState? state) => _state = state;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_document is null || _state is null || !_state.IsActive)
            return;

        foreach (var offset in _state.Cursors)
        {
            if (offset < 0 || offset > _document.TextLength)
                continue;

            DrawCursor(textView, drawingContext, offset);
        }
    }

    private static void DrawCursor(TextView textView, DrawingContext drawingContext, int offset)
    {
        var segment = new TextSegment { StartOffset = offset, Length = 0 };
        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            // Draw a vertical cursor line
            var top = new Point(rect.X, rect.Y);
            var bottom = new Point(rect.X, rect.Y + rect.Height);
            drawingContext.DrawLine(CursorPen, top, bottom);
        }
    }
}
