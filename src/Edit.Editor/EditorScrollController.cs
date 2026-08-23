namespace Edit.Editor;

/// <summary>Vertical scroll state for the editor viewport.</summary>
internal sealed class EditorScrollController
{
    public double ScrollY { get; private set; }

    public void ScrollByWheel(double deltaY)
    {
        ScrollY = Math.Max(0, ScrollY - deltaY * EditorLayout.LineHeight * 3);
    }

    public void EnsureCaretVisible(int caretLine, double viewportHeight)
    {
        var y = caretLine * EditorLayout.LineHeight;
        if (y < ScrollY)
            ScrollY = y;
        if (y + EditorLayout.LineHeight > ScrollY + viewportHeight)
            ScrollY = y + EditorLayout.LineHeight - viewportHeight;
        ScrollY = Math.Max(0, ScrollY);
    }
}
