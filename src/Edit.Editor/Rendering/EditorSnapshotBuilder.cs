using Edit.Core;
using Edit.Text;

namespace Edit.Editor.Rendering;

internal static class EditorSnapshotBuilder
{
    public static EditorRenderSnapshot Build(
        DocumentModel? document,
        EditorInteractionState state,
        double viewportHeight,
        IReadOnlyList<EditorDiagnostic>? diagnostics)
    {
        if (document is null)
            return EditorRenderSnapshot.Empty(state.Scroll.ScrollY);

        var buffer = document.Buffer;
        var scrollY = state.Scroll.ScrollY;
        var firstLine = (int)(scrollY / EditorLayout.LineHeight);
        var visible = (int)Math.Ceiling(viewportHeight / EditorLayout.LineHeight) + 2;
        var lastLine = Math.Min(buffer.LineCount - 1, firstLine + visible);
        firstLine = Math.Max(0, firstLine);

        var lines = new List<EditorLineSnapshot>(Math.Max(0, lastLine - firstLine + 1));
        for (var line = firstLine; line <= lastLine; line++)
        {
            lines.Add(new EditorLineSnapshot(
                line,
                buffer.GetLineContent(line),
                buffer.GetOffset(line, 0)));
        }

        var caret = buffer.GetPosition(document.CaretOffset);
        BracketMarkSnapshot? open = null;
        BracketMarkSnapshot? close = null;
        if (state.Syntax.Brackets is { } bp)
        {
            open = ToBracketMark(buffer, bp.OpenStart, bp.OpenEnd);
            close = ToBracketMark(buffer, bp.CloseStart, bp.CloseEnd);
        }

        var diagnosticMarks = (diagnostics ?? Array.Empty<EditorDiagnostic>())
            .Select(d => new DiagnosticMarkSnapshot(d.StartOffset, d.EndOffset, d.Severity))
            .ToArray();

        return new EditorRenderSnapshot(
            scrollY,
            lines,
            caret.Line,
            caret.Column,
            state.Syntax.Highlights.ToArray(),
            open,
            close,
            state.Selection.NormalizedStart,
            state.Selection.NormalizedEnd,
            diagnosticMarks,
            state.HoverText);
    }

    private static BracketMarkSnapshot? ToBracketMark(TextBuffer buffer, int start, int end)
    {
        var text = buffer.GetText(start, Math.Max(0, end - start));
        if (string.IsNullOrEmpty(text)) return null;
        var p = buffer.GetPosition(start);
        return new BracketMarkSnapshot(p.Line, p.Column, text);
    }
}
