using Edit.Core;
using Edit.Lsp.Protocol;
using Edit.Text;
using Microsoft.Extensions.Logging;

namespace Edit.Lsp;

/// <summary>
/// Compatibility façade over <see cref="ILanguageClientManager"/> for existing shell wiring.
/// </summary>
public sealed class LanguageClient : IAsyncDisposable
{
    private readonly ILanguageClientManager _manager;
    private readonly ILogger<LanguageClient> _logger;

    public LanguageClient(ILanguageClientManager manager, ILogger<LanguageClient> logger)
    {
        _manager = manager;
        _logger = logger;
        _manager.DiagnosticsChanged += (_, _) => DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Status => _manager.Status;
    public IReadOnlyList<DiagnosticItem> Diagnostics => _manager.Diagnostics;
    public event EventHandler? DiagnosticsChanged;
    public ILanguageClientManager Manager => _manager;

    public Task StartAsync(LanguageServerSettings settings, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("StartAsync(single) redirected to workspace start for {Id}", settings.Id);
        return _manager.StartWorkspaceAsync(workspaceRoot, cancellationToken);
    }

    public Task StartWorkspaceAsync(string workspaceRoot, CancellationToken cancellationToken = default) =>
        _manager.StartWorkspaceAsync(workspaceRoot, cancellationToken);

    public Task EnsureStartedForDocumentAsync(DocumentModel document, CancellationToken cancellationToken = default) =>
        _manager.EnsureStartedForDocumentAsync(document, cancellationToken);

    public Task DidOpenAsync(DocumentModel document) => _manager.DidOpenAsync(document);
    public Task DidChangeAsync(DocumentModel document) => _manager.DidChangeAsync(document);
    public Task DidCloseAsync(DocumentModel document) => _manager.DidCloseAsync(document);
    public Task DidSaveAsync(DocumentModel document) => _manager.DidSaveAsync(document);

    public Task<HoverResult?> HoverAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        _manager.HoverAsync(document, position, cancellationToken);

    public Task<IReadOnlyList<CompletionItem>> CompleteAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        _manager.CompleteAsync(document, position, cancellationToken);

    public Task<IReadOnlyList<Location>> DefinitionAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        _manager.DefinitionAsync(document, position, cancellationToken);

    public Task<IReadOnlyList<Location>> ReferencesAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        _manager.ReferencesAsync(document, position, cancellationToken);

    public Task<SignatureHelp?> SignatureHelpAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        _manager.SignatureHelpAsync(document, position, cancellationToken);

    public Task<WorkspaceEdit?> RenameAsync(DocumentModel document, TextPosition position, string newName, CancellationToken cancellationToken = default) =>
        _manager.RenameAsync(document, position, newName, cancellationToken);

    public Task<IReadOnlyList<TextEdit>> FormatDocumentAsync(DocumentModel document, CancellationToken cancellationToken = default) =>
        _manager.FormatDocumentAsync(document, cancellationToken);

    public Task<IReadOnlyList<CodeAction>> CodeActionsAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default) =>
        _manager.CodeActionsAsync(document, position, cancellationToken);

    public Task StopAsync() => _manager.StopAllAsync();
    public ValueTask DisposeAsync() => _manager.DisposeAsync();
}

/// <summary>In-memory mock LSP for tests — no external process.</summary>
public sealed class MockLanguageClient
{
    public string Status { get; set; } = "LSP: mock";
    public List<DiagnosticItem> Diagnostics { get; } = new();

    public Task StartAsync() => Task.CompletedTask;

    public HoverResult Hover(DocumentModel document, TextPosition position)
    {
        var word = document.Buffer.GetWordAt(position);
        var text = document.Buffer.GetValueInRange(word);
        return new HoverResult { Contents = string.IsNullOrEmpty(text) ? "(empty)" : $"mock hover: {text}", Kind = "plaintext" };
    }

    public IReadOnlyList<CompletionItem> Complete(DocumentModel document, TextPosition position)
    {
        var word = document.Buffer.GetWordAt(position);
        var prefix = document.Buffer.GetValueInRange(word);
        return new[]
        {
            new CompletionItem { Label = prefix + "Alpha", Detail = "mock" },
            new CompletionItem { Label = prefix + "Beta", Detail = "mock" }
        };
    }

    public void PublishSampleDiagnostic(int line, string message)
    {
        Diagnostics.Add(new DiagnosticItem
        {
            Line = line,
            Character = 0,
            EndLine = line,
            EndCharacter = 1,
            Message = message,
            Severity = "Warning",
            Source = "mock"
        });
    }
}
