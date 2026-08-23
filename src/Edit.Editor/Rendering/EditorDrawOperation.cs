using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Edit.Core;
using Edit.TreeSitter;
using SkiaSharp;

namespace Edit.Editor.Rendering;

internal sealed class EditorDrawOperation : ICustomDrawOperation
{
    private readonly EditorRenderSnapshot _snapshot;
    private readonly ISyntaxColorTheme _theme;

    public EditorDrawOperation(EditorRenderSnapshot snapshot, Rect bounds, ISyntaxColorTheme theme)
    {
        _snapshot = snapshot;
        _theme = theme;
        Bounds = bounds;
    }

    public Rect Bounds { get; }
    public void Dispose() { }
    public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
    public bool HitTest(Point p) => Bounds.Contains(p);

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature is null)
            return;

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
        canvas.Clear(new SKColor(30, 30, 30));

        using var font = EditorFonts.CreateFont();
        using var gutterPaint = new SKPaint { Color = new SKColor(80, 80, 80), IsAntialias = true };
        using var caretPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 1 };
        using var bracketPaint = new SKPaint { Color = new SKColor(255, 215, 0), IsAntialias = true };
        using var selectionPaint = new SKPaint { Color = new SKColor(38, 79, 120), IsAntialias = true };
        using var squigglePaint = new SKPaint { Color = new SKColor(244, 71, 71), StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        var charWidth = EditorLayout.CharWidth;
        var lineHeight = EditorLayout.LineHeight;
        var baseline = EditorLayout.Baseline;

        foreach (var line in _snapshot.Lines)
        {
            var y = (float)(line.LineNumber * lineHeight - _snapshot.ScrollY + baseline);
            canvas.DrawText((line.LineNumber + 1).ToString(), 8, y, font, gutterPaint);

            if (_snapshot.SelectionEnd > _snapshot.SelectionStart)
            {
                var lineStart = line.Offset;
                var lineEnd = lineStart + line.Content.Length;
                var selStart = Math.Max(_snapshot.SelectionStart, lineStart);
                var selEnd = Math.Min(_snapshot.SelectionEnd, lineEnd);
                if (selEnd > selStart)
                {
                    var x0 = (float)(EditorLayout.PaddingLeft + (selStart - lineStart) * charWidth);
                    var x1 = (float)(EditorLayout.PaddingLeft + (selEnd - lineStart) * charWidth);
                    var top = (float)(line.LineNumber * lineHeight - _snapshot.ScrollY);
                    canvas.DrawRect(x0, top, x1 - x0, lineHeight, selectionPaint);
                }
            }

            DrawHighlightedLine(canvas, font, line.Content, line.Offset, (float)EditorLayout.PaddingLeft, y, charWidth);

            foreach (var d in _snapshot.Diagnostics)
            {
                var lineStart = line.Offset;
                var lineEnd = lineStart + line.Content.Length;
                var a = Math.Max(d.Start, lineStart);
                var b = Math.Min(d.End, lineEnd);
                if (b > a)
                {
                    var x0 = (float)(EditorLayout.PaddingLeft + (a - lineStart) * charWidth);
                    var x1 = (float)(EditorLayout.PaddingLeft + (b - lineStart) * charWidth);
                    var baseY = (float)(line.LineNumber * lineHeight - _snapshot.ScrollY + lineHeight - 2);
                    squigglePaint.Color = d.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase)
                        ? new SKColor(204, 167, 0)
                        : new SKColor(244, 71, 71);
                    for (var x = x0; x < x1; x += 4)
                        canvas.DrawLine(x, baseY, Math.Min(x + 2, x1), baseY - 2, squigglePaint);
                }
            }
        }

        var cx = (float)(EditorLayout.PaddingLeft + _snapshot.CaretColumn * charWidth);
        var cy = (float)(_snapshot.CaretLine * lineHeight - _snapshot.ScrollY);
        canvas.DrawLine(cx, cy + 2, cx, cy + lineHeight - 2, caretPaint);

        if (_snapshot.OpenBracket is { } open)
            DrawBracketMark(canvas, font, open, bracketPaint, charWidth, lineHeight, baseline);
        if (_snapshot.CloseBracket is { } close)
            DrawBracketMark(canvas, font, close, bracketPaint, charWidth, lineHeight, baseline);

        if (!string.IsNullOrWhiteSpace(_snapshot.HoverText))
        {
            using var hoverBg = new SKPaint { Color = new SKColor(40, 40, 40), IsAntialias = true };
            using var hoverFg = new SKPaint { Color = SKColors.WhiteSmoke, IsAntialias = true };
            var hx = (float)(EditorLayout.PaddingLeft + _snapshot.CaretColumn * charWidth);
            var hy = (float)(_snapshot.CaretLine * lineHeight - _snapshot.ScrollY - 22);
            canvas.DrawRect(hx, hy, Math.Min(360, _snapshot.HoverText!.Length * 7 + 16), 20, hoverBg);
            canvas.DrawText(_snapshot.HoverText, hx + 6, hy + 14, font, hoverFg);
        }

        canvas.Restore();
    }

    private void DrawHighlightedLine(SKCanvas canvas, SKFont font, string content, int lineStart, float x, float y, float charWidth)
    {
        if (string.IsNullOrEmpty(content)) return;
        var i = 0;
        while (i < content.Length)
        {
            var abs = lineStart + i;
            var span = _snapshot.Highlights.FirstOrDefault(h => h.Start <= abs && abs < h.End);
            var end = content.Length;
            SKColor color = EditorSyntaxColors.Default(_theme);
            if (span.End > span.Start)
            {
                end = Math.Min(content.Length, span.End - lineStart);
                color = EditorSyntaxColors.ForCapture(_theme, span.CaptureName);
            }
            else
            {
                var next = _snapshot.Highlights
                    .Where(h => h.Start > abs)
                    .Select(h => h.Start - lineStart)
                    .DefaultIfEmpty(content.Length)
                    .Min();
                end = Math.Min(content.Length, next);
            }

            if (end <= i) end = i + 1;
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            // Draw one cell at a time on the monospace grid so caret/hit-test never drift
            // from Skia's multi-glyph string layout.
            for (var c = i; c < end; c++)
                canvas.DrawText(content[c].ToString(), x + c * charWidth, y, font, paint);
            i = end;
        }
    }

    private void DrawBracketMark(SKCanvas canvas, SKFont font, BracketMarkSnapshot mark, SKPaint paint, float charWidth, float lineHeight, float baseline)
    {
        var x = (float)(EditorLayout.PaddingLeft + mark.Column * charWidth);
        var y = (float)(mark.Line * lineHeight - _snapshot.ScrollY + baseline);
        canvas.DrawText(mark.Text, x, y, font, paint);
    }
}
