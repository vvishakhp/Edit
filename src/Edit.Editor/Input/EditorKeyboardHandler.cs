using Avalonia.Input;
using Edit.Core;
using Edit.Text;
using Edit.TreeSitter;

namespace Edit.Editor.Input;

internal sealed class EditorEditOperations
{
    private readonly EditorInteractionState _state;

    public EditorEditOperations(EditorInteractionState state) => _state = state;

    public TextRange ReplaceSelection(DocumentModel document, string text)
    {
        text ??= string.Empty;
        var previous = _state.Selection.Range;
        var sel = _state.Selection.Range;
        int start;
        int length;
        if (!sel.IsEmpty)
        {
            start = sel.Start;
            length = sel.Length;
        }
        else
        {
            start = document.CaretOffset;
            length = 0;
        }

        document.Buffer.ApplyEdit(start, length, text);
        document.CaretOffset = start + text.Length;
        _state.Selection.CollapseTo(document.CaretOffset);
        return previous;
    }

    public void ClearSelection(DocumentModel document) =>
        _state.Selection.CollapseTo(document.CaretOffset);
}

internal static class EditorKeyboardHandler
{
    public static bool TryHandle(
        KeyEventArgs e,
        DocumentModel document,
        ISyntaxHighlighter? highlighter,
        EditorInteractionState state,
        EditorEditOperations edits,
        double viewportHeight,
        Action<TextRange> notifySelectionChanged,
        Action refreshAfterEdit)
    {
        var buffer = document.Buffer;
        var caret = document.CaretOffset;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.Z)
            {
                buffer.Undo();
                document.CaretOffset = Math.Min(document.CaretOffset, buffer.Length);
                e.Handled = true;
                refreshAfterEdit();
                return true;
            }
            if (e.Key == Key.Y)
            {
                buffer.Redo();
                e.Handled = true;
                refreshAfterEdit();
                return true;
            }
        }

        switch (e.Key)
        {
            case Key.Left:
                document.CaretOffset = Math.Max(0, caret - 1);
                NotifySelectionAfterMove(state, document.CaretOffset, e.KeyModifiers, notifySelectionChanged);
                e.Handled = true;
                break;
            case Key.Right:
                document.CaretOffset = Math.Min(buffer.Length, caret + 1);
                NotifySelectionAfterMove(state, document.CaretOffset, e.KeyModifiers, notifySelectionChanged);
                e.Handled = true;
                break;
            case Key.Up:
            {
                var p = buffer.GetPosition(caret);
                document.CaretOffset = buffer.GetOffset(Math.Max(0, p.Line - 1), p.Column);
                NotifySelectionAfterMove(state, document.CaretOffset, e.KeyModifiers, notifySelectionChanged);
                e.Handled = true;
                break;
            }
            case Key.Down:
            {
                var p = buffer.GetPosition(caret);
                document.CaretOffset = buffer.GetOffset(Math.Min(buffer.LineCount - 1, p.Line + 1), p.Column);
                NotifySelectionAfterMove(state, document.CaretOffset, e.KeyModifiers, notifySelectionChanged);
                e.Handled = true;
                break;
            }
            case Key.Home:
            {
                var p = buffer.GetPosition(caret);
                document.CaretOffset = buffer.GetOffset(p.Line, 0);
                e.Handled = true;
                break;
            }
            case Key.End:
            {
                var p = buffer.GetPosition(caret);
                document.CaretOffset = buffer.GetOffset(p.Line, buffer.GetLineLength(p.Line));
                e.Handled = true;
                break;
            }
            case Key.Back:
                if (!state.Selection.Range.IsEmpty)
                {
                    notifySelectionChanged(edits.ReplaceSelection(document, string.Empty));
                }
                else if (caret > 0)
                {
                    buffer.ApplyEdit(caret - 1, 1, string.Empty);
                    document.CaretOffset = caret - 1;
                    edits.ClearSelection(document);
                }
                e.Handled = true;
                break;
            case Key.Delete:
                if (!state.Selection.Range.IsEmpty)
                {
                    notifySelectionChanged(edits.ReplaceSelection(document, string.Empty));
                }
                else if (caret < buffer.Length)
                {
                    buffer.ApplyEdit(caret, 1, string.Empty);
                    edits.ClearSelection(document);
                }
                e.Handled = true;
                break;
            case Key.Enter:
            {
                var indent = highlighter?.ComputeIndentOnEnter(
                    state.Selection.Range.IsEmpty ? caret : state.Selection.Range.Start,
                    EditorLayout.IndentSize) ?? 0;
                notifySelectionChanged(edits.ReplaceSelection(document, "\n" + new string(' ', indent)));
                e.Handled = true;
                break;
            }
            case Key.Tab:
                notifySelectionChanged(edits.ReplaceSelection(document, new string(' ', EditorLayout.IndentSize)));
                e.Handled = true;
                break;
        }

        if (!e.Handled)
            return false;

        var caretLine = buffer.GetPosition(document.CaretOffset).Line;
        state.Scroll.EnsureCaretVisible(caretLine, viewportHeight);
        state.Syntax.UpdateBrackets(highlighter, document.CaretOffset);
        refreshAfterEdit();
        return true;
    }

    private static void NotifySelectionAfterMove(
        EditorInteractionState state,
        int caretOffset,
        KeyModifiers modifiers,
        Action<TextRange> notifySelectionChanged)
    {
        var previous = state.Selection.Range;
        state.Selection.UpdateAfterCaretMove(caretOffset, modifiers.HasFlag(KeyModifiers.Shift));
        notifySelectionChanged(previous);
    }
}
