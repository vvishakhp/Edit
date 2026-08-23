using Edit.TreeSitter;

namespace Edit.Editor.Rendering;

internal readonly record struct EditorLineSnapshot(int LineNumber, string Content, int Offset);

internal readonly record struct BracketMarkSnapshot(int Line, int Column, string Text);

internal readonly record struct DiagnosticMarkSnapshot(int Start, int End, string Severity);

internal sealed class EditorRenderSnapshot
{
    public static EditorRenderSnapshot Empty(double scrollY) =>
        new(scrollY, Array.Empty<EditorLineSnapshot>(), 0, 0, Array.Empty<HighlightSpan>(), null, null, -1, -1, Array.Empty<DiagnosticMarkSnapshot>(), null);

    public EditorRenderSnapshot(
        double scrollY,
        IReadOnlyList<EditorLineSnapshot> lines,
        int caretLine,
        int caretColumn,
        IReadOnlyList<HighlightSpan> highlights,
        BracketMarkSnapshot? openBracket,
        BracketMarkSnapshot? closeBracket,
        int selectionStart,
        int selectionEnd,
        IReadOnlyList<DiagnosticMarkSnapshot> diagnostics,
        string? hoverText)
    {
        ScrollY = scrollY;
        Lines = lines;
        CaretLine = caretLine;
        CaretColumn = caretColumn;
        Highlights = highlights;
        OpenBracket = openBracket;
        CloseBracket = closeBracket;
        SelectionStart = selectionStart;
        SelectionEnd = selectionEnd;
        Diagnostics = diagnostics;
        HoverText = hoverText;
    }

    public double ScrollY { get; }
    public IReadOnlyList<EditorLineSnapshot> Lines { get; }
    public int CaretLine { get; }
    public int CaretColumn { get; }
    public IReadOnlyList<HighlightSpan> Highlights { get; }
    public BracketMarkSnapshot? OpenBracket { get; }
    public BracketMarkSnapshot? CloseBracket { get; }
    public int SelectionStart { get; }
    public int SelectionEnd { get; }
    public IReadOnlyList<DiagnosticMarkSnapshot> Diagnostics { get; }
    public string? HoverText { get; }
}
