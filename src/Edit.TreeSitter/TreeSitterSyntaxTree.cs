using TreeSitter;

namespace Edit.TreeSitter;

/// <summary>
/// Result of parsing source text with a Tree-sitter grammar native.
/// </summary>
public sealed class TreeSitterParseResult : IDisposable
{
    private Tree? _tree;

    public TreeSitterParseResult(
        string grammarId,
        string source,
        string rootType,
        string expression,
        bool hasError,
        Tree tree)
    {
        GrammarId = grammarId;
        Source = source;
        RootType = rootType;
        Expression = expression;
        HasError = hasError;
        _tree = tree;
        RootNode = tree.RootNode;
    }

    public string GrammarId { get; }
    public string Source { get; }
    public string RootType { get; }

    /// <summary>
    /// S-expression representation of the syntax tree (named nodes).
    /// </summary>
    public string Expression { get; }

    public bool HasError { get; }
    public Node RootNode { get; }

    public void Dispose()
    {
        _tree?.Dispose();
        _tree = null;
    }
}

/// <summary>
/// Loads Tree-sitter grammar natives and parses source into a syntax tree.
/// </summary>
public sealed class TreeSitterSyntaxTreeParser : IDisposable
{
    private readonly TreeSitterGrammarRegistry _registry;
    private Language? _language;
    private Parser? _parser;
    private string? _loadedGrammarId;

    public TreeSitterSyntaxTreeParser(TreeSitterGrammarRegistry registry)
    {
        _registry = registry;
    }

    public static TreeSitterSyntaxTreeParser CreateDefault(string? nativesRoot = null)
    {
        nativesRoot ??= Edit.Platform.AppPaths.TreeSitterNativesRoot();
        return new TreeSitterSyntaxTreeParser(TreeSitterGrammarRegistry.Load(nativesRoot));
    }

    public TreeSitterGrammarRegistry Registry => _registry;

    /// <summary>
    /// Parse <paramref name="source"/> with the grammar identified by <paramref name="grammarId"/>.
    /// Throws if the grammar is unknown or its native library cannot be loaded.
    /// </summary>
    public TreeSitterParseResult Parse(string grammarId, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grammarId);
        ArgumentNullException.ThrowIfNull(source);

        var normalized = TreeSitterGrammarRegistry.NormalizeGrammarId(grammarId);
        if (!_registry.TryGetEntry(normalized, out var entry))
            throw new InvalidOperationException($"Unknown Tree-sitter grammar '{grammarId}'.");

        var libraryPath = _registry.GetLibraryPath(normalized)
            ?? throw new InvalidOperationException(
                $"Tree-sitter native library for '{normalized}' was not found under the natives root.");

        if (!string.Equals(_loadedGrammarId, normalized, StringComparison.Ordinal))
        {
            _parser?.Dispose();
            _language?.Dispose();
            _language = new Language(libraryPath, entry.Function);
            _parser = new Parser { Language = _language };
            _loadedGrammarId = normalized;
        }

        if (_parser is null || _language is null)
            throw new InvalidOperationException($"Failed to initialize parser for '{normalized}'.");

        var tree = _parser.Parse(source)
            ?? throw new InvalidOperationException($"Tree-sitter returned no tree for '{normalized}'.");

        var root = tree.RootNode;
        return new TreeSitterParseResult(
            normalized,
            source,
            root.Type,
            root.Expression,
            root.HasError,
            tree);
    }

    /// <summary>
    /// Format a compact single-line S-expression by collapsing whitespace.
    /// Useful for stable golden-file comparisons.
    /// </summary>
    public static string FormatExpression(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return string.Join(' ',
            expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        _parser?.Dispose();
        _language?.Dispose();
    }
}
