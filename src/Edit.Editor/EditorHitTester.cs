using Avalonia;
using Edit.Text;

namespace Edit.Editor;

internal static class EditorHitTester
{
    public static int OffsetAt(Point point, double scrollY, TextBuffer buffer)
    {
        var line = (int)Math.Floor((point.Y + scrollY) / EditorLayout.LineHeight);
        line = Math.Clamp(line, 0, Math.Max(0, buffer.LineCount - 1));
        var col = (int)Math.Floor((point.X - EditorLayout.PaddingLeft) / EditorLayout.CharWidth);
        col = Math.Max(0, col);
        return buffer.GetOffset(line, col);
    }

    public static TextPosition PositionAt(Point point, double scrollY, TextBuffer buffer) =>
        buffer.GetPosition(OffsetAt(point, scrollY, buffer));

    public static TextRange WordAt(Point point, double scrollY, TextBuffer buffer) =>
        buffer.GetWordAt(OffsetAt(point, scrollY, buffer));
}
