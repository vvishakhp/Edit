using System.Text.RegularExpressions;
using Edit.ComponentModel;
using Edit.Core;
using Edit.Lsp.Protocol;
using Edit.Text;
using Microsoft.Extensions.Logging;

namespace Edit.Lsp;

public interface ILanguageClientManager : IAsyncDisposable
{
    string Status { get; }
    event EventHandler? DiagnosticsChanged;
    event EventHandler? StatusChanged;
    IReadOnlyList<DiagnosticItem> Diagnostics { get; }
    IReadOnlyList<LspSession> Sessions { get; }

    Task StartWorkspaceAsync(string workspaceRoot, CancellationToken cancellationToken = default);
    Task EnsureStartedForDocumentAsync(DocumentModel document, CancellationToken cancellationToken = default);
    Task StopAllAsync();
    LspSession? SessionForDocument(DocumentModel document);

    Task DidOpenAsync(DocumentModel document);
    Task DidChangeAsync(DocumentModel document);
    Task DidCloseAsync(DocumentModel document);
    Task DidSaveAsync(DocumentModel document);

    Task<HoverResult?> HoverAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CompletionItem>> CompleteAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Location>> DefinitionAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Location>> ReferencesAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default);
    Task<SignatureHelp?> SignatureHelpAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default);
    Task<WorkspaceEdit?> RenameAsync(DocumentModel document, TextPosition position, string newName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TextEdit>> FormatDocumentAsync(DocumentModel document, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CodeAction>> CodeActionsAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default);
    Task<SemanticTokens?> SemanticTokensFullAsync(DocumentModel document, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InlayHint>> InlayHintsAsync(DocumentModel document, CancellationToken cancellationToken = default);
}

public sealed class LanguageClientManager : ILanguageClientManager
{
    private readonly ILanguageServerRegistry _registry;
    private readonly ILspContributionRegistry _contributions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LanguageClientManager> _logger;
    private readonly List<LspSession> _sessions = new();
    private string? _workspaceRoot;

    public LanguageClientManager(
        ILanguageServerRegistry registry,
        ILspContributionRegistry contributions,
        ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _contributions = contributions;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<LanguageClientManager>();
    }

    public string Status { get; private set; } = "LSP: Off";
    public event EventHandler? DiagnosticsChanged;
    public event EventHandler? StatusChanged;
    public IReadOnlyList<LspSession> Sessions => _sessions;
    public string? WorkspaceRoot => _workspaceRoot;

    public IReadOnlyList<DiagnosticItem> Diagnostics =>
        _sessions.SelectMany(s => s.AllDiagnostics).ToList();

    public bool HasInitializedSessionFor(DocumentModel document) =>
        SessionForDocument(document)?.IsInitialized == true;

    public async Task EnsureStartedForDocumentAsync(DocumentModel document, CancellationToken cancellationToken = default)
    {
        if (document.Path is null) return;
        if (HasInitializedSessionFor(document)) return;

        var root = WorkspaceRootFinder.Find(document.Path, document.GrammarId);
        if (string.Equals(_workspaceRoot, root, StringComparison.OrdinalIgnoreCase)
            && _sessions.Any(s => s.IsInitialized))
            return;

        await StartWorkspaceAsync(root, cancellationToken);
    }

    public async Task StartWorkspaceAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        await StopAllAsync();
        _workspaceRoot = workspaceRoot;
        _registry.Reload();

        foreach (var settings in _registry.Servers)
        {
            var session = new LspSession(settings, _contributions, _loggerFactory.CreateLogger($"Lsp.{settings.Id}"));
            session.DiagnosticsChanged += (_, _) => DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
            await session.StartAsync(workspaceRoot, cancellationToken);
            _sessions.Add(session);
            if (session.IsInitialized)
                _logger.LogInformation("Started {Id}", settings.Id);
            else
                _logger.LogWarning("Server {Id} did not initialize ({Status})", settings.Id, session.Status);
        }

        RefreshStatus();
    }

    public async Task StopAllAsync()
    {
        foreach (var session in _sessions)
            await session.DisposeAsync();
        _sessions.Clear();
        _workspaceRoot = null;
        RefreshStatus();
    }

    public LspSession? SessionForDocument(DocumentModel document)
    {
        foreach (var session in _sessions)
        {
            if (!session.IsInitialized) continue;
            if (Matches(session.Settings, document))
                return session;
        }
        return null;
    }

    public Task DidOpenAsync(DocumentModel document) =>
        SessionForDocument(document)?.DidOpenAsync(document) ?? Task.CompletedTask;

    public Task DidChangeAsync(DocumentModel document) =>
        SessionForDocument(document)?.DidChangeAsync(document) ?? Task.CompletedTask;

    public Task DidCloseAsync(DocumentModel document) =>
        SessionForDocument(document)?.DidCloseAsync(document) ?? Task.CompletedTask;

    public Task DidSaveAsync(DocumentModel document) =>
        SessionForDocument(document)?.DidSaveAsync(document) ?? Task.CompletedTask;

    public Task<HoverResult?> HoverAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.HoverAsync(document, position, cancellationToken)
        ?? Task.FromResult<HoverResult?>(null);

    public Task<IReadOnlyList<CompletionItem>> CompleteAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.CompleteAsync(document, position, cancellationToken)
        ?? Task.FromResult<IReadOnlyList<CompletionItem>>(Array.Empty<CompletionItem>());

    public Task<IReadOnlyList<Location>> DefinitionAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.DefinitionAsync(document, position, cancellationToken)
        ?? Task.FromResult<IReadOnlyList<Location>>(Array.Empty<Location>());

    public Task<IReadOnlyList<Location>> ReferencesAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.ReferencesAsync(document, position, ct: cancellationToken)
        ?? Task.FromResult<IReadOnlyList<Location>>(Array.Empty<Location>());

    public Task<SignatureHelp?> SignatureHelpAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.SignatureHelpAsync(document, position, cancellationToken)
        ?? Task.FromResult<SignatureHelp?>(null);

    public Task<WorkspaceEdit?> RenameAsync(DocumentModel document, TextPosition position, string newName, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.RenameAsync(document, position, newName, cancellationToken)
        ?? Task.FromResult<WorkspaceEdit?>(null);

    public Task<IReadOnlyList<TextEdit>> FormatDocumentAsync(DocumentModel document, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.FormatDocumentAsync(document, cancellationToken)
        ?? Task.FromResult<IReadOnlyList<TextEdit>>(Array.Empty<TextEdit>());

    public Task<IReadOnlyList<CodeAction>> CodeActionsAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.CodeActionsAsync(document, position, cancellationToken)
        ?? Task.FromResult<IReadOnlyList<CodeAction>>(Array.Empty<CodeAction>());

    public Task<SemanticTokens?> SemanticTokensFullAsync(DocumentModel document, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.SemanticTokensFullAsync(document, cancellationToken)
        ?? Task.FromResult<SemanticTokens?>(null);

    public Task<IReadOnlyList<InlayHint>> InlayHintsAsync(DocumentModel document, CancellationToken cancellationToken = default) =>
        SessionForDocument(document)?.InlayHintsAsync(document, cancellationToken)
        ?? Task.FromResult<IReadOnlyList<InlayHint>>(Array.Empty<InlayHint>());

    public async ValueTask DisposeAsync() => await StopAllAsync();

    private void RefreshStatus()
    {
        var live = _sessions.Where(s => s.IsInitialized).ToList();
        Status = live.Count == 0
            ? (_sessions.Count == 0 ? "LSP: Off" : "LSP: Unavailable")
            : live.Count == 1
                ? live[0].Status
                : $"LSP: {live.Count} servers";
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public static bool Matches(LanguageServerSettings settings, DocumentModel document)
    {
        if (document.Path is null) return false;

        if (settings.LanguageIds.Count > 0 && document.GrammarId is not null)
        {
            if (settings.LanguageIds.Any(id => string.Equals(id, document.GrammarId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        foreach (var glob in settings.FileGlobs)
        {
            if (GlobMatcher.IsMatch(document.Path, glob))
                return true;
        }

        return false;
    }
}

public static class GlobMatcher
{
    public static bool IsMatch(string path, string glob)
    {
        if (string.IsNullOrWhiteSpace(glob) || glob is "*" or "**/*") return true;
        path = path.Replace('\\', '/');
        glob = glob.Replace('\\', '/');

        // Simple: **/*.ext or *.ext
        var starExt = Regex.Match(glob, @"\*\.([A-Za-z0-9]+)$");
        if (starExt.Success)
            return path.EndsWith("." + starExt.Groups[1].Value, StringComparison.OrdinalIgnoreCase);

        var pattern = "^" + Regex.Escape(glob)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", ".") + "$";
        return Regex.IsMatch(path, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
