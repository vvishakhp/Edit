using Edit.Text;
using Edit.Platform;
using TreeSitter;

namespace Edit.TreeSitter;

public readonly record struct HighlightSpan(int Start, int End, string CaptureName);

public readonly record struct BracketPair(int OpenStart, int OpenEnd, int CloseStart, int CloseEnd);

public interface ISyntaxHighlighter
{
    string? GrammarId { get; }
    bool UsesNativeTreeSitter { get; }
    IReadOnlyList<HighlightSpan> GetHighlights(int start, int end);
    BracketPair? FindMatchingBracket(int caretOffset);
    int ComputeIndentOnEnter(int caretOffset, int indentSize = 4);
    void UpdateDocument(TextBufferSnapshot snapshot, string? grammarId);
}

/// <summary>
/// Tree-sitter highlighter using self-built native parsers and upstream query files.
/// </summary>
public sealed class TreeSitterHighlighter : ISyntaxHighlighter, IDisposable
{
    private readonly TreeSitterGrammarRegistry _registry;
    private string _text = string.Empty;
    private readonly List<HighlightSpan> _spans = new();
    private Language? _language;
    private Parser? _parser;
    private Tree? _tree;
    private string? _loadedGrammarId;

    public TreeSitterHighlighter(TreeSitterGrammarRegistry registry)
    {
        _registry = registry;
    }

    public static TreeSitterHighlighter CreateDefault(string? nativesRoot = null)
    {
        nativesRoot ??= AppPaths.TreeSitterNativesRoot();
        var registry = TreeSitterGrammarRegistry.Load(nativesRoot);
        return new TreeSitterHighlighter(registry);
    }

    public string? GrammarId { get; private set; }
    public bool UsesNativeTreeSitter { get; private set; }

    public void UpdateDocument(TextBufferSnapshot snapshot, string? grammarId)
    {
        _text = snapshot.Text ?? string.Empty;
        GrammarId = grammarId;
        _spans.Clear();
        _tree?.Dispose();
        _tree = null;
        UsesNativeTreeSitter = false;

        if (string.IsNullOrEmpty(grammarId) || string.IsNullOrEmpty(_text))
            return;

        if (TryParseNative(grammarId))
        {
            UsesNativeTreeSitter = true;
            ApplyHighlightQuery();
        }
    }

    public IReadOnlyList<HighlightSpan> GetHighlights(int start, int end)
    {
        if (_spans.Count == 0) return Array.Empty<HighlightSpan>();
        return _spans.Where(s => s.End > start && s.Start < end).ToList();
    }

    public BracketPair? FindMatchingBracket(int caretOffset)
    {
        caretOffset = Math.Clamp(caretOffset, 0, _text.Length);
        if (_tree is not null && _language is not null)
        {
            var fromTree = FindBracketViaTreeWalk(caretOffset);
            if (fromTree is not null) return fromTree;
        }

        return FindBracketFallback(caretOffset);
    }

    public int ComputeIndentOnEnter(int caretOffset, int indentSize = 4)
    {
        caretOffset = Math.Clamp(caretOffset, 0, _text.Length);
        var baseIndent = CountLeadingIndent(caretOffset, indentSize);

        if (_tree is not null && TryGetIndentDeltaFromQuery(caretOffset, out var delta))
            return Math.Max(0, baseIndent + delta * indentSize);

        var lineStart = caretOffset;
        while (lineStart > 0 && _text[lineStart - 1] is not ('\n' or '\r'))
            lineStart--;
        var open = 0;
        for (var i = lineStart; i < caretOffset; i++)
        {
            if (_text[i] is '(' or '[' or '{') open++;
            else if (_text[i] is ')' or ']' or '}') open = Math.Max(0, open - 1);
        }

        return baseIndent + (open > 0 ? indentSize : 0);
    }

    public void Dispose()
    {
        _tree?.Dispose();
        _parser?.Dispose();
        _language?.Dispose();
    }

    private bool TryParseNative(string grammarId)
    {
        var normalized = TreeSitterGrammarRegistry.NormalizeGrammarId(grammarId);
        if (!_registry.TryGetEntry(normalized, out var entry))
            return false;

        var libraryPath = _registry.GetLibraryPath(normalized);
        if (libraryPath is null)
            return false;

        try
        {
            if (!string.Equals(_loadedGrammarId, normalized, StringComparison.Ordinal))
            {
                _parser?.Dispose();
                _language?.Dispose();
                _language = new Language(libraryPath, entry.Function);
                _parser = new Parser { Language = _language };
                _loadedGrammarId = normalized;
            }

            if (_parser is null || _language is null) return false;
            _tree = _parser.Parse(_text);
            return _tree is not null;
        }
        catch
        {
            _tree?.Dispose();
            _tree = null;
            return false;
        }
    }

    private void ApplyHighlightQuery()
    {
        if (_tree is null || _language is null || GrammarId is null) return;
        var source = _registry.GetQuerySource(GrammarId, "highlights");
        if (string.IsNullOrWhiteSpace(source)) return;

        try
        {
            using var query = new Query(_language, source);
            foreach (var capture in query.Execute(_tree.RootNode).Captures)
            {
                var start = capture.Node.StartIndex;
                var end = capture.Node.EndIndex;
                if (end > start)
                    _spans.Add(new HighlightSpan(start, end, capture.Name));
            }
        }
        catch
        {
            // Invalid query for this grammar version
        }
    }

    private BracketPair? FindBracketViaTreeWalk(int caretOffset)
    {
        if (_tree is null) return null;
        char? ch = null;
        var at = caretOffset;
        if (at < _text.Length && IsBracket(_text[at])) ch = _text[at];
        else if (at > 0 && IsBracket(_text[at - 1])) { at--; ch = _text[at]; }
        if (ch is null) return null;

        var node = _tree.RootNode.NamedDescendantForIndex(at) ?? _tree.RootNode.DescendantForIndex(at);
        var parent = node?.Parent;
        if (parent is null) return FindBracketFallback(caretOffset);

        var openers = "([{";
        var map = new Dictionary<char, char> { ['('] = ')', ['['] = ']', ['{'] = '}', [')'] = '(', [']'] = '[', ['}'] = '{' };
        var match = map[ch.Value];
        if (openers.Contains(ch.Value))
        {
            var depth = 0;
            foreach (var child in parent.Children)
            {
                if (child.StartIndex < at) continue;
                if (child.Text == ch.ToString()) depth++;
                else if (child.Text == match.ToString())
                {
                    depth--;
                    if (depth == 0)
                        return new BracketPair(at, at + 1, child.StartIndex, child.EndIndex);
                }
            }
        }

        return FindBracketFallback(caretOffset);
    }

    private bool TryGetIndentDeltaFromQuery(int caretOffset, out int delta)
    {
        delta = 0;
        if (_language is null || GrammarId is null) return false;
        var source = _registry.GetQuerySource(GrammarId, "indents");
        if (string.IsNullOrWhiteSpace(source)) return false;

        var i = caretOffset - 1;
        while (i >= 0 && char.IsWhiteSpace(_text[i]) && _text[i] is not ('\n' or '\r')) i--;
        if (i < 0) return false;
        if (_text[i] is '{' or '(' or '[') { delta = 1; return true; }
        if (_text[i] is '}' or ')' or ']') { delta = 0; return true; }
        if (_text[i] == ':' && TreeSitterGrammarRegistry.NormalizeGrammarId(GrammarId) == "python")
        {
            delta = 1;
            return true;
        }
        return source.Contains("@indent.increase", StringComparison.Ordinal);
    }

    private int CountLeadingIndent(int caretOffset, int indentSize)
    {
        var lineStart = caretOffset;
        while (lineStart > 0 && _text[lineStart - 1] is not ('\n' or '\r'))
            lineStart--;
        var indent = 0;
        for (var i = lineStart; i < caretOffset; i++)
        {
            if (_text[i] == ' ') indent++;
            else if (_text[i] == '\t') indent += indentSize;
            else break;
        }
        return indent;
    }

    private BracketPair? FindBracketFallback(int caretOffset)
    {
        if (_text.Length == 0) return null;
        var pairs = new Dictionary<char, char>
        {
            ['('] = ')', ['['] = ']', ['{'] = '}',
            [')'] = '(', [']'] = '[', ['}'] = '{'
        };
        char? ch = null;
        var at = caretOffset;
        if (at < _text.Length && pairs.ContainsKey(_text[at])) ch = _text[at];
        else if (at > 0 && pairs.ContainsKey(_text[at - 1])) { at--; ch = _text[at]; }
        if (ch is null) return null;
        var match = pairs[ch.Value];
        var isOpen = "([{".Contains(ch.Value);
        if (isOpen)
        {
            var depth = 0;
            for (var i = at; i < _text.Length; i++)
            {
                if (_text[i] == ch) depth++;
                else if (_text[i] == match)
                {
                    depth--;
                    if (depth == 0) return new BracketPair(at, at + 1, i, i + 1);
                }
            }
        }
        else
        {
            var depth = 0;
            for (var i = at; i >= 0; i--)
            {
                if (_text[i] == ch) depth++;
                else if (_text[i] == match)
                {
                    depth--;
                    if (depth == 0) return new BracketPair(i, i + 1, at, at + 1);
                }
            }
        }
        return null;
    }

    private static bool IsBracket(char c) => c is '(' or ')' or '[' or ']' or '{' or '}';
}

internal static class NodeIndexExtensions
{
    public static Node? DescendantForIndex(this Node node, int index)
    {
        try
        {
            var m = node.GetType().GetMethod("DescendantForIndex", new[] { typeof(int) });
            if (m?.Invoke(node, new object[] { index }) is Node n) return n;
        }
        catch { /* ignore */ }

        Node? best = node;
        foreach (var child in node.Children)
        {
            if (index >= child.StartIndex && index < child.EndIndex)
                return DescendantForIndex(child, index) ?? child;
            if (index == child.EndIndex)
                best = child;
        }
        return best;
    }

    public static Node? NamedDescendantForIndex(this Node node, int index)
    {
        try
        {
            var m = node.GetType().GetMethod("NamedDescendantForIndex", new[] { typeof(int) });
            if (m?.Invoke(node, new object[] { index }) is Node n) return n;
        }
        catch { /* ignore */ }

        return DescendantForIndex(node, index);
    }
}
