using Edit.Core;
using Edit.TreeSitter;

namespace Edit.Editor;

/// <summary>Cached syntax highlights and bracket matching for the active document.</summary>
internal sealed class EditorSyntaxState
{
    private IReadOnlyList<HighlightSpan> _highlights = Array.Empty<HighlightSpan>();

    public IReadOnlyList<HighlightSpan> Highlights => _highlights;
    public BracketPair? Brackets { get; private set; }

    public void Refresh(DocumentModel? document, ISyntaxHighlighter? highlighter)
    {
        if (document is null || highlighter is null)
        {
            _highlights = Array.Empty<HighlightSpan>();
            Brackets = null;
            return;
        }

        highlighter.UpdateDocument(document.Buffer.CreateSnapshot(), document.GrammarId);
        _highlights = highlighter.GetHighlights(0, document.Buffer.Length);
        Brackets = highlighter.FindMatchingBracket(document.CaretOffset);
    }

    public void UpdateBrackets(ISyntaxHighlighter? highlighter, int caretOffset) =>
        Brackets = highlighter?.FindMatchingBracket(caretOffset);

    public HighlightSpan? GetHighlightAt(int offset)
    {
        foreach (var span in _highlights)
        {
            if (span.Start <= offset && offset < span.End)
                return span;
        }
        return null;
    }
}
