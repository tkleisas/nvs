using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;

namespace NVS.Controls;

/// <summary>
/// A minimap control that renders a scaled-down overview of the editor document.
/// Shows current viewport position and allows click-to-scroll navigation.
/// </summary>
public sealed class MinimapControl : Control
{
    private TextEditor? _editor;
    private string[] _lines = [];
    private bool _isDragging;

    private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromArgb(120, 0xD4, 0xD4, 0xD4));
    private static readonly IBrush KeywordBrush = new SolidColorBrush(Color.FromArgb(140, 0x56, 0x9C, 0xD6));
    private static readonly IBrush StringBrush = new SolidColorBrush(Color.FromArgb(140, 0xCE, 0x91, 0x78));
    private static readonly IBrush CommentBrush = new SolidColorBrush(Color.FromArgb(100, 0x6A, 0x99, 0x55));
    private static readonly IBrush ViewportBrush = new SolidColorBrush(Color.FromArgb(30, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush ViewportBorderBrush = new SolidColorBrush(Color.FromArgb(60, 0xFF, 0xFF, 0xFF));
    private static readonly IPen ViewportPen = new Pen(ViewportBorderBrush, 1);

    private const double LineHeight = 2.0;
    private const double CharWidth = 1.2;
    private const int MaxVisibleChars = 80;

    public void AttachEditor(TextEditor editor)
    {
        if (_editor != null)
        {
            _editor.TextChanged -= OnEditorTextChanged;
            _editor.TextArea.TextView.ScrollOffsetChanged -= OnScrollChanged;
        }

        _editor = editor;
        _editor.TextChanged += OnEditorTextChanged;
        _editor.TextArea.TextView.ScrollOffsetChanged += OnScrollChanged;
        UpdateLines();
        InvalidateVisual();
    }

    public void DetachEditor()
    {
        if (_editor != null)
        {
            _editor.TextChanged -= OnEditorTextChanged;
            _editor.TextArea.TextView.ScrollOffsetChanged -= OnScrollChanged;
            _editor = null;
        }
        _lines = [];
        InvalidateVisual();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        UpdateLines();
        InvalidateVisual();
    }

    private void OnScrollChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private void UpdateLines()
    {
        if (_editor?.Document is null)
        {
            _lines = [];
            return;
        }

        _lines = _editor.Document.Text.Split('\n');
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, bounds.Width, bounds.Height));

        if (_lines.Length == 0 || _editor is null)
            return;

        var totalLines = _lines.Length;
        var totalHeight = totalLines * LineHeight;
        var scale = totalHeight > bounds.Height ? bounds.Height / totalHeight : 1.0;
        var scaledLineHeight = LineHeight * scale;

        for (var i = 0; i < _lines.Length; i++)
        {
            var y = i * scaledLineHeight;
            if (y > bounds.Height)
                break;

            var line = _lines[i].TrimEnd('\r');
            DrawMinimapLine(context, line, y, scaledLineHeight, bounds.Width);
        }

        DrawViewportOverlay(context, scaledLineHeight, bounds);
    }

    private static void DrawMinimapLine(DrawingContext context, string line, double y, double lineHeight, double maxWidth)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var indent = 0;
        while (indent < line.Length && (line[indent] == ' ' || line[indent] == '\t'))
            indent++;

        var x = indent * CharWidth;
        var contentLength = Math.Min(line.Length - indent, MaxVisibleChars);
        var width = Math.Min(contentLength * CharWidth, maxWidth - x);

        if (width <= 0)
            return;

        var brush = GetLineBrush(line, indent);
        context.DrawRectangle(brush, null, new Rect(x, y, width, Math.Max(lineHeight, 1)));
    }

    private static IBrush GetLineBrush(string line, int indent)
    {
        if (indent >= line.Length)
            return TextBrush;

        var trimmed = line.AsSpan(indent);
        if (trimmed.StartsWith("//") || trimmed.StartsWith("#") || trimmed.StartsWith("--"))
            return CommentBrush;
        if (trimmed.Length > 0 && (trimmed[0] == '"' || trimmed[0] == '\''))
            return StringBrush;
        if (StartsWithKeyword(trimmed))
            return KeywordBrush;

        return TextBrush;
    }

    private static bool StartsWithKeyword(ReadOnlySpan<char> span)
    {
        ReadOnlySpan<string> keywords =
        [
            "public", "private", "protected", "class", "interface", "struct", "enum",
            "void", "int", "string", "bool", "var", "return", "if", "else", "for",
            "while", "using", "namespace", "import", "from", "def", "function", "const",
            "let", "async", "await", "static", "sealed", "override", "virtual",
        ];

        foreach (var kw in keywords)
        {
            if (span.StartsWith(kw.AsSpan()) &&
                (span.Length == kw.Length || !char.IsLetterOrDigit(span[kw.Length])))
                return true;
        }
        return false;
    }

    private void DrawViewportOverlay(DrawingContext context, double scaledLineHeight, Rect bounds)
    {
        if (_editor?.TextArea.TextView is null)
            return;

        var textView = _editor.TextArea.TextView;
        var editorLineHeight = textView.DefaultLineHeight;
        if (editorLineHeight <= 0)
            return;

        var firstVisibleLine = (int)(textView.ScrollOffset.Y / editorLineHeight);
        var visibleLines = (int)(textView.Bounds.Height / editorLineHeight);

        var viewportY = firstVisibleLine * scaledLineHeight;
        var viewportHeight = Math.Max(visibleLines * scaledLineHeight, 10);
        viewportY = Math.Max(0, Math.Min(viewportY, bounds.Height - viewportHeight));

        context.DrawRectangle(ViewportBrush, ViewportPen,
            new Rect(0, viewportY, bounds.Width, viewportHeight));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isDragging = true;
        e.Pointer.Capture(this);
        ScrollToMinimapPosition(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
        {
            ScrollToMinimapPosition(e.GetPosition(this).Y);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    private void ScrollToMinimapPosition(double y)
    {
        if (_editor?.TextArea.TextView is null || _lines.Length == 0)
            return;

        var bounds = Bounds;
        var totalLines = _lines.Length;
        var totalHeight = totalLines * LineHeight;
        var scale = totalHeight > bounds.Height ? bounds.Height / totalHeight : 1.0;
        var scaledLineHeight = LineHeight * scale;

        if (scaledLineHeight <= 0)
            return;

        var targetLine = (int)(y / scaledLineHeight);
        targetLine = Math.Clamp(targetLine, 0, totalLines - 1);

        // ScrollToLine is 1-based
        _editor.ScrollToLine(targetLine + 1);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(80, availableSize.Height);
    }
}
