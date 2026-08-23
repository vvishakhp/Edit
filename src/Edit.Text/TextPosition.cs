namespace Edit.Text;

/// <summary>Zero-based line and UTF-16 column.</summary>
public readonly record struct TextPosition(int Line, int Column)
{
    public override string ToString() => $"({Line},{Column})";
}

/// <summary>Half-open UTF-16 offset range [Start, End).</summary>
public readonly record struct TextRange(int Start, int End)
{
    public int Length => End - Start;
    public bool IsEmpty => Start >= End;
}

public enum EndOfLineKind
{
    Lf,
    Crlf
}
