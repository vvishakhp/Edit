using Edit.ComponentModel;
using Edit.Text;
using Microsoft.Extensions.Logging;

namespace Edit.Core;

public abstract class PluginContext : IPluginContext
{
    protected PluginContext(IServiceProvider services, ICommandRegistry commands, IToolWindowRegistry toolWindows)
    {
        Services = services;
        Commands = commands;
        ToolWindows = toolWindows;
    }

    public IServiceProvider Services { get; }
    public ICommandRegistry Commands { get; }
    public IToolWindowRegistry ToolWindows { get; }
}

public sealed class DefaultPluginContext : PluginContext
{
    public DefaultPluginContext(IServiceProvider services, ICommandRegistry commands, IToolWindowRegistry toolWindows)
        : base(services, commands, toolWindows)
    {
    }
}

public sealed class DocumentModel
{
    private bool _isDirty;

    public DocumentModel(string? path, TextBuffer buffer)
    {
        Path = path;
        Buffer = buffer;
        Id = Guid.NewGuid();
        Title = path is null ? "Untitled" : System.IO.Path.GetFileName(path);
        buffer.Changed += (_, _) => IsDirty = true;
    }

    public Guid Id { get; }
    public string? Path { get; set; }
    public string Title { get; set; }
    public TextBuffer Buffer { get; }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? GrammarId { get; set; }
    public int CaretOffset { get; set; }
    public TextPosition CaretPosition => Buffer.GetPosition(CaretOffset);

    public event EventHandler? DirtyChanged;

    /// <summary>Tab / window title including a dirty marker when needed.</summary>
    public string DisplayTitle => IsDirty ? $"{Title} *" : Title;
}

public sealed class DocumentService
{
    private readonly List<DocumentModel> _documents = new();
    public IReadOnlyList<DocumentModel> Documents => _documents;
    public DocumentModel? ActiveDocument { get; private set; }
    public event EventHandler? ActiveDocumentChanged;
    public event EventHandler? DocumentsChanged;

    public DocumentModel CreateUntitled(string? initialText = null)
    {
        var doc = new DocumentModel(null, new TextBuffer(initialText ?? string.Empty));
        _documents.Add(doc);
        SetActive(doc);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        return doc;
    }

    public DocumentModel OpenFile(string path)
    {
        var existing = _documents.FirstOrDefault(d =>
            d.Path is not null && string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SetActive(existing);
            return existing;
        }

        var buffer = TextBuffer.FromFile(path);
        var doc = new DocumentModel(path, buffer)
        {
            GrammarId = GrammarMapper.FromPath(path)
        };
        _documents.Add(doc);
        SetActive(doc);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        return doc;
    }

    public void Save(DocumentModel document, string? path = null)
    {
        path ??= document.Path ?? throw new InvalidOperationException("No path for document.");
        document.Buffer.SaveToFile(path);
        document.Path = path;
        document.Title = System.IO.Path.GetFileName(path);
        document.IsDirty = false;
        document.GrammarId = GrammarMapper.FromPath(path);
    }

    public void SetActive(DocumentModel? document)
    {
        ActiveDocument = document;
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Close(DocumentModel document)
    {
        _documents.Remove(document);
        if (ActiveDocument == document)
            SetActive(_documents.LastOrDefault());
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }
}

public static class GrammarMapper
{
    public static string? FromPath(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".json" or ".jsonc" => "json",
            ".js" or ".mjs" or ".cjs" => "javascript",
            ".ts" => "typescript",
            ".tsx" => "tsx",
            ".jsx" => "javascript",
            ".py" => "python",
            ".xml" or ".csproj" or ".xaml" => "xml",
            ".md" => "markdown",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".c" or ".h" => "c",
            ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh" => "cpp",
            ".go" => "go",
            ".rs" => "rust",
            ".rb" => "ruby",
            ".java" => "java",
            ".php" => "php",
            ".scala" or ".sc" => "scala",
            ".sh" or ".bash" or ".zsh" => "bash",
            ".toml" => "toml",
            ".jl" => "julia",
            ".hs" or ".lhs" => "haskell",
            ".ml" or ".mli" => "ocaml",
            ".ql" => "ql",
            ".sv" or ".v" => "verilog",
            ".agda" => "agda",
            ".ejs" or ".erb" => "embedded-template",
            _ => null
        };
    }
}

public sealed class StatusBarModel : System.ComponentModel.INotifyPropertyChanged
{
    private string _lineColumn = "Ln 1, Col 1";
    private string _language = "Plain Text";
    private string _encoding = "UTF-8";
    private string _eol = "LF";
    private string _lspStatus = "LSP: Off";
    private string _dirty = "";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string LineColumn
    {
        get => _lineColumn;
        set => SetField(ref _lineColumn, value, nameof(LineColumn));
    }

    public string Language
    {
        get => _language;
        set => SetField(ref _language, value, nameof(Language));
    }

    public string Encoding
    {
        get => _encoding;
        set => SetField(ref _encoding, value, nameof(Encoding));
    }

    public string Eol
    {
        get => _eol;
        set => SetField(ref _eol, value, nameof(Eol));
    }

    public string LspStatus
    {
        get => _lspStatus;
        set => SetField(ref _lspStatus, value, nameof(LspStatus));
    }

    public string Dirty
    {
        get => _dirty;
        set => SetField(ref _dirty, value, nameof(Dirty));
    }

    public void UpdateFrom(DocumentModel? doc, string? lspStatus = null)
    {
        if (doc is null)
        {
            LineColumn = "Ln —, Col —";
            Language = "Plain Text";
            Dirty = "";
            Eol = "LF";
        }
        else
        {
            var p = doc.CaretPosition;
            LineColumn = $"Ln {p.Line + 1}, Col {p.Column + 1}";
            Language = FormatLanguage(doc.GrammarId);
            Dirty = doc.IsDirty ? "●" : "";
            Eol = doc.Buffer.Eol == EndOfLineKind.Crlf ? "CRLF" : "LF";
        }

        if (lspStatus is not null)
            LspStatus = lspStatus;
    }

    private static string FormatLanguage(string? grammarId) =>
        string.IsNullOrWhiteSpace(grammarId) ? "Plain Text" : $"Tree-sitter: {grammarId}";

    private void SetField(ref string field, string value, string propertyName)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SettingsModel
{
    public int SchemaVersion { get; set; } = 1;
    public List<LanguageServerSettings> LanguageServers { get; set; } = new();
    public Dictionary<string, string> Keybindings { get; set; } = new();
    public string? LayoutJson { get; set; }
}

public sealed class LanguageServerSettings
{
    public string Id { get; set; } = "";
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = new();
    public List<string> FileGlobs { get; set; } = new() { "*" };
}
