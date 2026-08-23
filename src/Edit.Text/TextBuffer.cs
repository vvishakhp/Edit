namespace Edit.Text;

/// <summary>
/// Piece-table text buffer with line metadata.
/// Pieces reference either the original load buffer or an append-only add buffer.
/// Line/offset conversion is O(log n) via a binary-searchable line-start index maintained on edit.
/// Offsets are UTF-16 code units (LSP-compatible).
/// </summary>
public sealed class TextBuffer
{
    private string _original;
    private readonly System.Text.StringBuilder _add = new();
    private readonly List<Piece> _pieces = new();
    private readonly List<int> _lineStarts = new() { 0 }; // UTF-16 offsets of each line start
    private readonly Stack<EditSnapshot> _undo = new();
    private readonly Stack<EditSnapshot> _redo = new();

    public TextBuffer(string? text = null, EndOfLineKind eol = EndOfLineKind.Lf)
    {
        Eol = eol;
        _original = text ?? string.Empty;
        if (_original.Length > 0)
            _pieces.Add(new Piece(BufferKind.Original, 0, _original.Length));
        RebuildLineStarts();
        Version = 1;
    }

    public EndOfLineKind Eol { get; private set; }
    public int Version { get; private set; }
    public int Length { get; private set; }
    public int LineCount => _lineStarts.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public event EventHandler<TextChangedEventArgs>? Changed;

    public TextPosition GetPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        var line = FindLineStartIndex(offset);
        return new TextPosition(line, offset - _lineStarts[line]);
    }

    public int GetOffset(int line, int column)
    {
        if (LineCount == 0) return 0;
        line = Math.Clamp(line, 0, LineCount - 1);
        var lineStart = _lineStarts[line];
        var lineEnd = line + 1 < LineCount ? _lineStarts[line + 1] : Length;
        // Exclude trailing EOL from max column when not last line
        var maxCol = lineEnd - lineStart;
        if (line + 1 < LineCount)
        {
            var eolLen = EolLengthAt(lineEnd);
            maxCol = Math.Max(0, maxCol - eolLen);
        }
        column = Math.Clamp(column, 0, maxCol);
        return lineStart + column;
    }

    public string GetLineContent(int line)
    {
        if (line < 0 || line >= LineCount) return string.Empty;
        var start = _lineStarts[line];
        var end = line + 1 < LineCount ? _lineStarts[line + 1] : Length;
        if (line + 1 < LineCount)
            end -= EolLengthAt(end);
        return GetText(start, end - start);
    }

    public int GetLineLength(int line) => GetLineContent(line).Length;

    public string GetText() => GetText(0, Length);

    public string GetText(int start, int length)
    {
        if (length <= 0 || Length == 0) return string.Empty;
        start = Math.Clamp(start, 0, Length);
        length = Math.Min(length, Length - start);
        var sb = new System.Text.StringBuilder(length);
        var remaining = length;
        var offset = 0;
        foreach (var piece in _pieces)
        {
            if (remaining <= 0) break;
            if (offset + piece.Length <= start)
            {
                offset += piece.Length;
                continue;
            }

            var localStart = Math.Max(0, start - offset);
            var take = Math.Min(piece.Length - localStart, remaining);
            sb.Append(GetPieceText(piece).Slice(localStart, take));
            remaining -= take;
            offset += piece.Length;
            start = offset;
        }
        return sb.ToString();
    }

    public string GetValueInRange(TextRange range) =>
        GetText(range.Start, Math.Max(0, range.Length));

    public TextRange GetWordAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        if (Length == 0) return new TextRange(0, 0);

        var pos = GetPosition(offset);
        var line = GetLineContent(pos.Line);
        if (line.Length == 0) return new TextRange(offset, offset);

        var col = Math.Clamp(pos.Column, 0, line.Length);
        if (col > 0 && col == line.Length) col--;

        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

        if (!IsWord(line[col]))
        {
            var lineStart = _lineStarts[pos.Line];
            return new TextRange(lineStart + col, lineStart + col);
        }

        var start = col;
        while (start > 0 && IsWord(line[start - 1])) start--;
        var end = col;
        while (end < line.Length && IsWord(line[end])) end++;
        var abs = _lineStarts[pos.Line];
        return new TextRange(abs + start, abs + end);
    }

    public TextRange GetWordAt(TextPosition position) =>
        GetWordAt(GetOffset(position.Line, position.Column));

    public void ApplyEdit(int start, int length, string text, bool recordUndo = true)
    {
        start = Math.Clamp(start, 0, Length);
        length = Math.Clamp(length, 0, Length - start);
        text ??= string.Empty;

        if (recordUndo)
        {
            _undo.Push(new EditSnapshot(start, length, GetText(start, length), text));
            _redo.Clear();
        }

        DeleteRange(start, length);
        if (text.Length > 0)
            Insert(start, text);

        Version++;
        RebuildLineStarts();
        Changed?.Invoke(this, new TextChangedEventArgs(start, length, text.Length));
    }

    public void ApplyEdit(TextRange range, string text, bool recordUndo = true) =>
        ApplyEdit(range.Start, range.Length, text, recordUndo);

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var snap = _undo.Pop();
        // Revert: delete inserted text, restore deleted
        ApplyEdit(snap.Start, snap.Inserted.Length, snap.Deleted, recordUndo: false);
        _redo.Push(snap);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var snap = _redo.Pop();
        ApplyEdit(snap.Start, snap.Deleted.Length, snap.Inserted, recordUndo: false);
        _undo.Push(snap);
        return true;
    }

    public TextBufferSnapshot CreateSnapshot() =>
        new(GetText(), Version, Eol, LineCount);

    public static TextBuffer FromFile(string path)
    {
        var text = File.ReadAllText(path);
        var eol = text.Contains("\r\n", StringComparison.Ordinal) ? EndOfLineKind.Crlf : EndOfLineKind.Lf;
        return new TextBuffer(text, eol);
    }

    public void SaveToFile(string path)
    {
        var text = GetText();
        File.WriteAllText(path, text);
    }

    /// <summary>Convert UTF-16 offset to approximate byte offset for UTF-8 (BMP-focused MVP).</summary>
    public int Utf16ToUtf8ByteOffset(int utf16Offset)
    {
        var text = GetText(0, Math.Clamp(utf16Offset, 0, Length));
        return System.Text.Encoding.UTF8.GetByteCount(text);
    }

    private void Insert(int offset, string text)
    {
        var addStart = _add.Length;
        _add.Append(text);
        var newPiece = new Piece(BufferKind.Add, addStart, text.Length);
        InsertPieceAt(offset, newPiece);
        Length += text.Length;
    }

    private void DeleteRange(int start, int length)
    {
        if (length <= 0) return;
        var end = start + length;
        var newPieces = new List<Piece>();
        var offset = 0;
        foreach (var piece in _pieces)
        {
            var pieceEnd = offset + piece.Length;
            if (pieceEnd <= start || offset >= end)
            {
                newPieces.Add(piece);
            }
            else
            {
                if (offset < start)
                    newPieces.Add(piece with { Length = start - offset });
                if (pieceEnd > end)
                {
                    var skip = end - offset;
                    newPieces.Add(new Piece(piece.Kind, piece.Start + skip, piece.Length - skip));
                }
            }
            offset = pieceEnd;
        }
        _pieces.Clear();
        _pieces.AddRange(newPieces.Where(p => p.Length > 0));
        Length -= length;
    }

    private void InsertPieceAt(int offset, Piece piece)
    {
        if (_pieces.Count == 0)
        {
            _pieces.Add(piece);
            return;
        }

        var newPieces = new List<Piece>();
        var pos = 0;
        var inserted = false;
        foreach (var existing in _pieces)
        {
            var end = pos + existing.Length;
            if (!inserted && offset <= pos)
            {
                newPieces.Add(piece);
                inserted = true;
            }

            if (offset > pos && offset < end)
            {
                var leftLen = offset - pos;
                newPieces.Add(existing with { Length = leftLen });
                newPieces.Add(piece);
                newPieces.Add(new Piece(existing.Kind, existing.Start + leftLen, existing.Length - leftLen));
                inserted = true;
            }
            else
            {
                newPieces.Add(existing);
            }
            pos = end;
        }

        if (!inserted)
            newPieces.Add(piece);

        _pieces.Clear();
        _pieces.AddRange(newPieces.Where(p => p.Length > 0));
    }

    private ReadOnlySpan<char> GetPieceText(Piece piece) =>
        piece.Kind == BufferKind.Original
            ? _original.AsSpan(piece.Start, piece.Length)
            : _add.ToString(piece.Start, piece.Length).AsSpan();

    private void RebuildLineStarts()
    {
        _lineStarts.Clear();
        _lineStarts.Add(0);
        var offset = 0;
        foreach (var piece in _pieces)
        {
            var span = GetPieceText(piece);
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i] == '\n')
                    _lineStarts.Add(offset + i + 1);
                else if (span[i] == '\r')
                {
                    if (i + 1 < span.Length && span[i + 1] == '\n')
                    {
                        _lineStarts.Add(offset + i + 2);
                        i++;
                    }
                    else
                        _lineStarts.Add(offset + i + 1);
                }
            }
            offset += piece.Length;
        }
        Length = offset;
    }

    private int FindLineStartIndex(int offset)
    {
        var lo = 0;
        var hi = _lineStarts.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_lineStarts[mid] <= offset)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return Math.Max(0, hi);
    }

    private int EolLengthAt(int lineEndOffset)
    {
        if (lineEndOffset <= 0) return 0;
        if (lineEndOffset >= 2 && GetText(lineEndOffset - 2, 2) == "\r\n") return 2;
        if (lineEndOffset >= 1)
        {
            var c = GetText(lineEndOffset - 1, 1);
            if (c is "\n" or "\r") return 1;
        }
        return 0;
    }

    private enum BufferKind { Original, Add }

    private readonly record struct Piece(BufferKind Kind, int Start, int Length);

    private sealed record EditSnapshot(int Start, int DeletedLength, string Deleted, string Inserted);
}

public sealed class TextChangedEventArgs : EventArgs
{
    public TextChangedEventArgs(int start, int deletedLength, int insertedLength)
    {
        Start = start;
        DeletedLength = deletedLength;
        InsertedLength = insertedLength;
    }

    public int Start { get; }
    public int DeletedLength { get; }
    public int InsertedLength { get; }
}

public sealed class TextBufferSnapshot
{
    public TextBufferSnapshot(string text, int version, EndOfLineKind eol, int lineCount)
    {
        Text = text;
        Version = version;
        Eol = eol;
        LineCount = lineCount;
    }

    public string Text { get; }
    public int Version { get; }
    public EndOfLineKind Eol { get; }
    public int LineCount { get; }
    public int Length => Text.Length;
}
