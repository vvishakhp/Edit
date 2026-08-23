using System.Text.Json;

namespace Edit.TreeSitter;

public sealed record TreeSitterGrammarEntry(
    string Id,
    string Repository,
    string Ref,
    string Library,
    string Function,
    string? Subpath = null);

public sealed class TreeSitterGrammarManifest
{
    public string Platform { get; init; } = "linux-x64";
    public string TreeSitterDotNetVersion { get; init; } = "1.3.0";
    public List<TreeSitterGrammarEntry> Grammars { get; init; } = new();
}

/// <summary>
/// Resolves self-built Tree-sitter grammar libraries and query files from the app output directory.
/// </summary>
public sealed class TreeSitterGrammarRegistry
{
    private readonly string _nativesRoot;
    private readonly TreeSitterGrammarManifest _manifest;
    private readonly Dictionary<string, TreeSitterGrammarEntry> _byId;

    public TreeSitterGrammarRegistry(string nativesRoot, TreeSitterGrammarManifest manifest)
    {
        _nativesRoot = nativesRoot;
        _manifest = manifest;
        _byId = manifest.Grammars.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
    }

    public static TreeSitterGrammarRegistry Load(string nativesRoot, string? manifestPath = null)
    {
        manifestPath ??= Path.Combine(nativesRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            var fallback = FindManifestInTree(nativesRoot);
            if (fallback is not null)
                manifestPath = fallback;
        }

        TreeSitterGrammarManifest manifest;
        if (File.Exists(manifestPath))
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<ManifestFileDto>(json, JsonOptions)?.ToManifest()
                       ?? new TreeSitterGrammarManifest();
        }
        else
        {
            manifest = new TreeSitterGrammarManifest();
        }

        return new TreeSitterGrammarRegistry(nativesRoot, manifest);
    }

    public IReadOnlyDictionary<string, TreeSitterGrammarEntry> Grammars => _byId;

    public bool TryGetEntry(string grammarId, out TreeSitterGrammarEntry entry)
    {
        var id = NormalizeGrammarId(grammarId);
        return _byId.TryGetValue(id, out entry!);
    }

    public string? GetLibraryPath(string grammarId)
    {
        if (!TryGetEntry(grammarId, out var entry)) return null;
        var path = Path.Combine(_nativesRoot, entry.Id, entry.Library);
        return File.Exists(path) ? path : null;
    }

    public string? GetQuerySource(string grammarId, string kind)
    {
        if (!TryGetEntry(grammarId, out var entry)) return null;

        var queriesDir = Path.Combine(_nativesRoot, entry.Id, "queries");
        var fileName = kind switch
        {
            "highlights" => "highlights.scm",
            "indents" => "indents.scm",
            "brackets" => "brackets.scm",
            _ => $"{kind}.scm"
        };
        var path = Path.Combine(queriesDir, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static string NormalizeGrammarId(string grammarId) => grammarId.ToLowerInvariant() switch
    {
        "cs" => "csharp",
        "js" or "jsx" => "javascript",
        "ts" => "typescript",
        "jsonc" => "json",
        "py" => "python",
        "rb" => "ruby",
        "rs" => "rust",
        "go" or "golang" => "go",
        "java" => "java",
        "cpp" or "cxx" or "cc" => "cpp",
        "sh" or "zsh" => "bash",
        _ => grammarId.ToLowerInvariant()
    };

    private static string? FindManifestInTree(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "config", "tree-sitter-grammars.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ManifestFileDto
    {
        public string? Platform { get; set; }
        public string? TreeSitterDotNetVersion { get; set; }
        public List<TreeSitterGrammarEntry>? Grammars { get; set; }

        public TreeSitterGrammarManifest ToManifest() => new()
        {
            Platform = Platform ?? "linux-x64",
            TreeSitterDotNetVersion = TreeSitterDotNetVersion ?? "1.3.0",
            Grammars = Grammars ?? new List<TreeSitterGrammarEntry>()
        };
    }
}
