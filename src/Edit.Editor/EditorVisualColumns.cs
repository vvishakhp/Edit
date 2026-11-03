namespace Edit.Editor;

/// <summary>Tab ↔ visual-column mapping for monospace rendering and hit-testing.</summary>
public static class EditorVisualColumns
{
    public static int TabSize => EditorLayout.IndentSize;

    /// <summary>Visual column at a character index within a line (0-based).</summary>
    public static int GetVisualColumn(ReadOnlySpan<char> line, int charIndex)
    {
        charIndex = Math.Clamp(charIndex, 0, line.Length);
        var col = 0;
        for (var i = 0; i < charIndex; i++)
            col = Advance(col, line[i]);
        return col;
    }

    /// <summary>Character index for a visual column within a line (0-based).</summary>
    public static int GetCharIndex(ReadOnlySpan<char> line, int visualColumn)
    {
        visualColumn = Math.Max(0, visualColumn);
        var col = 0;
        for (var i = 0; i < line.Length; i++)
        {
            var next = Advance(col, line[i]);
            if (next > visualColumn)
                return i;
            col = next;
            if (col == visualColumn)
                return i + 1;
        }
        return line.Length;
    }

    public static int Advance(int visualColumn, char ch) =>
        ch == '\t' ? (visualColumn / TabSize + 1) * TabSize : visualColumn + 1;

    public static int MeasureWidth(ReadOnlySpan<char> text)
    {
        var col = 0;
        foreach (var ch in text)
            col = Advance(col, ch);
        return col;
    }
}
