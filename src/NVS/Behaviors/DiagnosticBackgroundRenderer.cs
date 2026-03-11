using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using NVS.Core.Interfaces;

namespace NVS.Behaviors;

/// <summary>
/// Renders squiggly underlines beneath text ranges that have diagnostics.
/// Uses IBackgroundRenderer to draw wavy lines under error/warning regions.
/// </summary>
public sealed class DiagnosticBackgroundRenderer : IBackgroundRenderer
{
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0x47, 0x47)); // Red
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.FromRgb(0xCD, 0x9B, 0x31)); // Yellow/amber
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)); // Blue
    private static readonly IBrush HintBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)); // Gray

    private IReadOnlyList<Diagnostic> _diagnostics = [];
    private TextDocument? _document;

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetDocument(TextDocument? document)
    {
        _document = document;
    }

    public void UpdateDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_document is null || _diagnostics.Count == 0)
            return;

        foreach (var diagnostic in _diagnostics)
        {
            DrawDiagnostic(textView, drawingContext, diagnostic);
        }
    }

    private void DrawDiagnostic(TextView textView, DrawingContext drawingContext, Diagnostic diagnostic)
    {
        if (_document is null)
            return;

        var startLine = diagnostic.Range.Start.Line + 1; // LSP is 0-based, AvaloniaEdit is 1-based
        var endLine = diagnostic.Range.End.Line + 1;

        if (startLine < 1 || startLine > _document.LineCount)
            return;

        try
        {
            var startOffset = GetOffset(_document, diagnostic.Range.Start.Line, diagnostic.Range.Start.Column);
            var endOffset = GetOffset(_document, diagnostic.Range.End.Line, diagnostic.Range.End.Column);

            if (startOffset >= endOffset)
            {
                // Zero-width range: underline the whole token/line
                var line = _document.GetLineByNumber(startLine);
                startOffset = line.Offset;
                endOffset = line.EndOffset;
            }

            var brush = GetBrush(diagnostic.Severity);
            var pen = new Pen(brush, 1.5, lineCap: PenLineCap.Round);

            var segment = new TextSegment { StartOffset = startOffset, EndOffset = endOffset };

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                DrawSquiggly(drawingContext, pen, rect.BottomLeft, rect.BottomRight);
            }
        }
        catch
        {
            // Offsets may be stale if document was edited — just skip
        }
    }

    private static void DrawSquiggly(DrawingContext ctx, Pen pen, Point start, Point end)
    {
        const double amplitude = 2.0;
        const double wavelength = 4.0;

        var geometry = new StreamGeometry();
        using (var sgc = geometry.Open())
        {
            sgc.BeginFigure(start, false);
            var x = start.X;
            var baseY = start.Y;
            var up = true;

            while (x < end.X)
            {
                var nextX = Math.Min(x + wavelength, end.X);
                var nextY = up ? baseY - amplitude : baseY;
                sgc.LineTo(new Point(nextX, nextY));
                x = nextX;
                up = !up;
            }

            sgc.EndFigure(false);
        }

        ctx.DrawGeometry(null, pen, geometry);
    }

    private static int GetOffset(TextDocument doc, int line, int column)
    {
        // LSP uses 0-based line/column; AvaloniaEdit uses 1-based
        var lineNumber = Math.Clamp(line + 1, 1, doc.LineCount);
        var docLine = doc.GetLineByNumber(lineNumber);
        var col = Math.Clamp(column, 0, docLine.Length);
        return docLine.Offset + col;
    }

    private static IBrush GetBrush(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => ErrorBrush,
        DiagnosticSeverity.Warning => WarningBrush,
        DiagnosticSeverity.Information => InfoBrush,
        DiagnosticSeverity.Hint => HintBrush,
        _ => InfoBrush,
    };
}

/// <summary>
/// Minimal TextSegment for use with BackgroundGeometryBuilder.
/// </summary>
file sealed class TextSegment : ISegment
{
    public int Offset => StartOffset;
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public int Length => EndOffset - StartOffset;
}
