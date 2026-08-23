using Edit.Text;

namespace Edit.Editor;

/// <summary>Tracks anchor-based text selection as UTF-16 offsets.</summary>
internal sealed class EditorSelection
{
    private int _anchor = -1;
    private int _start = -1;
    private int _end = -1;

    public int NormalizedStart =>
        _start < 0 || _end < 0 ? -1 : Math.Min(_start, _end);

    public int NormalizedEnd =>
        _start < 0 || _end < 0 ? -1 : Math.Max(_start, _end);

    public TextRange Range
    {
        get
        {
            if (_start < 0 || _end < 0)
                return default;
            return new TextRange(NormalizedStart, NormalizedEnd);
        }
    }

    public void CollapseTo(int offset)
    {
        _anchor = offset;
        _start = offset;
        _end = offset;
    }

    public void SetRange(int start, int end)
    {
        _start = start;
        _end = end;
    }

    public void UpdateFromAnchor(int caretOffset)
    {
        if (_anchor < 0)
            _anchor = caretOffset;
        SetRange(_anchor, caretOffset);
    }

    public void UpdateAfterCaretMove(int caretOffset, bool extendSelection)
    {
        if (extendSelection)
        {
            if (_anchor < 0)
                _anchor = caretOffset;
            UpdateFromAnchor(caretOffset);
        }
        else
        {
            CollapseTo(caretOffset);
        }
    }

    public void SelectWord(TextBuffer buffer, int offset, out int caretOffset)
    {
        var word = buffer.GetWordAt(offset);
        _anchor = word.Start;
        SetRange(word.Start, word.End);
        caretOffset = word.End;
    }

    public void SelectLine(TextBuffer buffer, int offset, out int caretOffset)
    {
        var line = buffer.GetPosition(offset).Line;
        var start = buffer.GetOffset(line, 0);
        var end = buffer.GetOffset(line, buffer.GetLineLength(line));
        _anchor = start;
        SetRange(start, end);
        caretOffset = end;
    }

    public bool HasChangedSince(TextRange previous) =>
        previous.Start != Range.Start || previous.End != Range.End;
}
