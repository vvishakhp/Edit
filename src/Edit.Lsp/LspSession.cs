using System.Text.Json;
using Edit.ComponentModel;
using Edit.Core;
using Edit.Lsp.Protocol;
using Edit.Lsp.Transport;
using Edit.Text;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Edit.Lsp;

public sealed class LspSession : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly LanguageServerSettings _settings;
    private readonly ILspContributionRegistry _contributions;
    private readonly Dictionary<string, List<DiagnosticItem>> _diagnosticsByUri = new(StringComparer.OrdinalIgnoreCase);
    private ILspTransport? _transport;
    private JsonRpc? _rpc;
    private bool _initialized;

    public LspSession(
        LanguageServerSettings settings,
        ILspContributionRegistry contributions,
        ILogger logger)
    {
        _settings = settings;
        _contributions = contributions;
        _logger = logger;
    }

    public string Id => _settings.Id;
    public LanguageServerSettings Settings => _settings;
    public string Status { get; private set; } = "LSP: Off";
    public ServerCapabilities? Capabilities { get; private set; }
    public bool IsInitialized => _initialized;
    public event EventHandler? DiagnosticsChanged;

    public IReadOnlyDictionary<string, IReadOnlyList<DiagnosticItem>> DiagnosticsByUri =>
        _diagnosticsByUri.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<DiagnosticItem>)kv.Value,
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DiagnosticItem> AllDiagnostics =>
        _diagnosticsByUri.Values.SelectMany(x => x).ToList();

    public async Task StartAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        await StopAsync();

        if (!string.Equals(_settings.Protocol, "lsp", StringComparison.OrdinalIgnoreCase))
        {
            Status = $"LSP: skip {_settings.Id} (protocol)";
            return;
        }

        try
        {
            _transport = await CreateTransportAsync(workspaceRoot, cancellationToken);
            var formatter = new SystemTextJsonFormatter
            {
                JsonSerializerOptions =
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }
            };
            var handler = new HeaderDelimitedMessageHandler(
                _transport.SendingStream,
                _transport.ReceivingStream,
                formatter);
            _rpc = new JsonRpc(handler);
            RegisterServerToClientHandlers(_rpc);
            _rpc.StartListening();

            var rootUri = new Uri(
                workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? workspaceRoot
                    : workspaceRoot + Path.DirectorySeparatorChar).AbsoluteUri;

            var initResult = await _rpc.InvokeWithParameterObjectAsync<InitializeResult>(
                "initialize",
                new
                {
                    processId = Environment.ProcessId,
                    rootUri,
                    capabilities = BuildClientCapabilities(),
                    clientInfo = new { name = "Edit", version = "0.1.0" },
                    initializationOptions = _settings.InitializationOptions
                },
                cancellationToken);

            Capabilities = initResult.Capabilities;
            await _rpc.NotifyWithParameterObjectAsync("initialized", new { });
            _initialized = true;
            Status = $"LSP: {_settings.DisplayName}";
            _logger.LogInformation("Language server {Id} started via {Transport}", _settings.Id, _transport.Description);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start language server {Id}", _settings.Id);
            Status = $"LSP: {_settings.Id} error";
            await StopAsync();
        }
    }

    public async Task DidOpenAsync(DocumentModel document)
    {
        if (!_initialized || _rpc is null || document.Path is null) return;
        var uri = ToUri(document.Path);
        await _rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri,
                languageId = document.GrammarId ?? "plaintext",
                version = document.Buffer.Version,
                text = document.Buffer.GetText()
            }
        });
    }

    public async Task DidChangeAsync(DocumentModel document)
    {
        if (!_initialized || _rpc is null || document.Path is null) return;
        var uri = ToUri(document.Path);
        await _rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri, version = document.Buffer.Version },
            contentChanges = new[] { new { text = document.Buffer.GetText() } }
        });
    }

    public async Task DidCloseAsync(DocumentModel document)
    {
        if (!_initialized || _rpc is null || document.Path is null) return;
        var uri = ToUri(document.Path);
        await _rpc.NotifyWithParameterObjectAsync("textDocument/didClose", new
        {
            textDocument = new { uri }
        });
        _diagnosticsByUri.Remove(uri);
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DidSaveAsync(DocumentModel document)
    {
        if (!_initialized || _rpc is null || document.Path is null) return;
        var uri = ToUri(document.Path);
        await _rpc.NotifyWithParameterObjectAsync("textDocument/didSave", new
        {
            textDocument = new { uri },
            text = document.Buffer.GetText()
        });
    }

    public Task<HoverResult?> HoverAsync(DocumentModel document, TextPosition position, CancellationToken ct = default) =>
        RequestPositionAsync(document, position, "textDocument/hover", ParseHover, ct);

    public async Task<IReadOnlyList<CompletionItem>> CompleteAsync(
        DocumentModel document, TextPosition position, CancellationToken ct = default)
    {
        if (!CanRequest(document, Capabilities?.CompletionProvider)) return Array.Empty<CompletionItem>();
        try
        {
            var raw = await _rpc!.InvokeWithParameterObjectAsync<JsonElement>(
                "textDocument/completion",
                PositionParams(document, position),
                ct);
            return ParseCompletions(raw);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "completion failed");
            return Array.Empty<CompletionItem>();
        }
    }

    public Task<IReadOnlyList<Location>> DefinitionAsync(DocumentModel document, TextPosition position, CancellationToken ct = default) =>
        RequestLocationsAsync(document, position, "textDocument/definition", Capabilities?.DefinitionProvider, ct);

    public Task<IReadOnlyList<Location>> DeclarationAsync(DocumentModel document, TextPosition position, CancellationToken ct = default) =>
        RequestLocationsAsync(document, position, "textDocument/declaration", Capabilities?.DeclarationProvider, ct);

    public Task<IReadOnlyList<Location>> TypeDefinitionAsync(DocumentModel document, TextPosition position, CancellationToken ct = default) =>
        RequestLocationsAsync(document, position, "textDocument/typeDefinition", Capabilities?.TypeDefinitionProvider, ct);

    public Task<IReadOnlyList<Location>> ImplementationAsync(DocumentModel document, TextPosition position, CancellationToken ct = default) =>
        RequestLocationsAsync(document, position, "textDocument/implementation", Capabilities?.ImplementationProvider, ct);

    public async Task<IReadOnlyList<Location>> ReferencesAsync(
        DocumentModel document, TextPosition position, bool includeDeclaration = true, CancellationToken ct = default)
    {
        if (!CanRequest(document, Capabilities?.ReferencesProvider)) return Array.Empty<Location>();
        try
        {
            var raw = await _rpc!.InvokeWithParameterObjectAsync<JsonElement>(
                "textDocument/references",
                new
                {
                    textDocument = new { uri = ToUri(document.Path!) },
                    position = new { line = position.Line, character = position.Column },
                    context = new { includeDeclaration }
                },
                ct);
            return ParseLocations(raw);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "references failed");
            return Array.Empty<Location>();
        }
    }

    public async Task<SignatureHelp?> SignatureHelpAsync(DocumentModel document, TextPosition position, CancellationToken ct = default)
    {
        if (!CanRequest(document, Capabilities?.SignatureHelpProvider)) return null;
        try
        {
            return await _rpc!.InvokeWithParameterObjectAsync<SignatureHelp?>(
                "textDocument/signatureHelp",
                PositionParams(document, position),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "signatureHelp failed");
            return null;
        }
    }

    public async Task<WorkspaceEdit?> RenameAsync(
        DocumentModel document, TextPosition position, string newName, CancellationToken ct = default)
    {
        if (!CanRequest(document, Capabilities?.RenameProvider)) return null;
        try
        {
            return await _rpc!.InvokeWithParameterObjectAsync<WorkspaceEdit?>(
                "textDocument/rename",
                new
                {
                    textDocument = new { uri = ToUri(document.Path!) },
                    position = new { line = position.Line, character = position.Column },
                    newName
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "rename failed");
            return null;
        }
    }

    public async Task<IReadOnlyList<TextEdit>> FormatDocumentAsync(DocumentModel document, CancellationToken ct = default)
    {
        if (!CanRequest(document, Capabilities?.DocumentFormattingProvider)) return Array.Empty<TextEdit>();
        try
        {
            var edits = await _rpc!.InvokeWithParameterObjectAsync<TextEdit[]?>(
                "textDocument/formatting",
                new
                {
                    textDocument = new { uri = ToUri(document.Path!) },
                    options = new { tabSize = 4, insertSpaces = true }
                },
                ct);
            return edits ?? Array.Empty<TextEdit>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "formatting failed");
            return Array.Empty<TextEdit>();
        }
    }

    public async Task<IReadOnlyList<CodeAction>> CodeActionsAsync(
        DocumentModel document, TextPosition position, CancellationToken ct = default)
    {
        if (!CanRequest(document, Capabilities?.CodeActionProvider)) return Array.Empty<CodeAction>();
        try
        {
            var range = new Protocol.Range
            {
                Start = new Position(position.Line, position.Column),
                End = new Position(position.Line, position.Column)
            };
            var raw = await _rpc!.InvokeWithParameterObjectAsync<JsonElement>(
                "textDocument/codeAction",
                new
                {
                    textDocument = new { uri = ToUri(document.Path!) },
                    range,
                    context = new { diagnostics = Array.Empty<object>() }
                },
                ct);
            if (raw.ValueKind != JsonValueKind.Array) return Array.Empty<CodeAction>();
            return raw.Deserialize<CodeAction[]>() ?? Array.Empty<CodeAction>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "codeAction failed");
            return Array.Empty<CodeAction>();
        }
    }

    public async Task<SemanticTokens?> SemanticTokensFullAsync(DocumentModel document, CancellationToken ct = default)
    {
        if (!CanRequest(document, Capabilities?.SemanticTokensProvider)) return null;
        try
        {
            return await _rpc!.InvokeWithParameterObjectAsync<SemanticTokens?>(
                "textDocument/semanticTokens/full",
                new { textDocument = new { uri = ToUri(document.Path!) } },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "semanticTokens failed");
            return null;
        }
    }

    public async Task<IReadOnlyList<InlayHint>> InlayHintsAsync(DocumentModel document, CancellationToken ct = default)
    {
        if (!CanRequest(document, Capabilities?.InlayHintProvider)) return Array.Empty<InlayHint>();
        try
        {
            var lineCount = Math.Max(0, document.Buffer.LineCount - 1);
            var hints = await _rpc!.InvokeWithParameterObjectAsync<InlayHint[]?>(
                "textDocument/inlayHint",
                new
                {
                    textDocument = new { uri = ToUri(document.Path!) },
                    range = new
                    {
                        start = new { line = 0, character = 0 },
                        end = new { line = lineCount, character = 0 }
                    }
                },
                ct);
            return hints ?? Array.Empty<InlayHint>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "inlayHint failed");
            return Array.Empty<InlayHint>();
        }
    }

    public async Task<JsonElement?> SendRequestAsync(string method, object? @params, CancellationToken ct = default)
    {
        if (!_initialized || _rpc is null) return null;
        try
        {
            return await _rpc.InvokeWithParameterObjectAsync<JsonElement>(method, @params ?? new { }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "custom request {Method} failed", method);
            return null;
        }
    }

    public async Task ExecuteCommandAsync(string command, object?[]? arguments, CancellationToken ct = default)
    {
        var local = _contributions.GetCommand(command);
        if (local is not null)
        {
            await local.ExecuteAsync(arguments, ct);
            return;
        }

        if (!_initialized || _rpc is null) return;
        await _rpc.InvokeWithParameterObjectAsync(
            "workspace/executeCommand",
            new { command, arguments },
            ct);
    }

    public async Task StopAsync()
    {
        try
        {
            if (_initialized && _rpc is not null)
            {
                try { await _rpc.InvokeAsync("shutdown"); }
                catch { /* ignore */ }
                try { await _rpc.NotifyAsync("exit"); }
                catch { /* ignore */ }
            }
        }
        finally
        {
            _rpc?.Dispose();
            _rpc = null;
            if (_transport is not null)
            {
                await _transport.DisposeAsync();
                _transport = null;
            }
            _initialized = false;
            Capabilities = null;
            _diagnosticsByUri.Clear();
            Status = "LSP: Off";
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task<ILspTransport> CreateTransportAsync(string workspaceRoot, CancellationToken ct)
    {
        if (string.Equals(_settings.Transport, "socket", StringComparison.OrdinalIgnoreCase))
        {
            var host = _settings.Host ?? "127.0.0.1";
            var port = _settings.Port ?? throw new InvalidOperationException($"Server {_settings.Id} socket transport requires Port");
            return await SocketLspTransport.ConnectAsync(host, port, ct);
        }

        if (string.IsNullOrWhiteSpace(_settings.Command))
            throw new InvalidOperationException($"Server {_settings.Id} has empty command");

        return StdioLspTransport.Start(_settings.Command, _settings.Args, workspaceRoot);
    }

    private void RegisterServerToClientHandlers(JsonRpc rpc)
    {
        rpc.AddLocalRpcMethod("textDocument/publishDiagnostics", new Action<PublishDiagnosticsParams>(PublishDiagnostics));
        rpc.AddLocalRpcMethod("window/logMessage", new Action<JsonElement>(p =>
            _logger.LogDebug("LSP log: {Message}", p.ToString())));
        rpc.AddLocalRpcMethod("window/showMessage", new Action<JsonElement>(p =>
            _logger.LogInformation("LSP message: {Message}", p.ToString())));
        rpc.AddLocalRpcMethod("window/workDoneProgress/create", new Func<JsonElement, object>(_ => new { }));

        foreach (var handler in _contributions.Methods)
        {
            var h = handler;
            if (h.IsRequest)
            {
                rpc.AddLocalRpcMethod(h.Method, new Func<JsonElement, Task<object?>>(async p =>
                    await h.HandleAsync(p)));
            }
            else
            {
                rpc.AddLocalRpcMethod(h.Method, new Action<JsonElement>(p =>
                    _ = h.HandleAsync(p)));
            }
        }
    }

    private void PublishDiagnostics(PublishDiagnosticsParams @params)
    {
        var list = @params.Diagnostics.Select(d => new DiagnosticItem
        {
            Message = d.Message,
            Severity = d.Severity switch
            {
                DiagnosticSeverity.Error => "Error",
                DiagnosticSeverity.Warning => "Warning",
                DiagnosticSeverity.Information => "Info",
                DiagnosticSeverity.Hint => "Hint",
                _ => "Info"
            },
            Line = d.Range.Start.Line,
            Character = d.Range.Start.Character,
            EndLine = d.Range.End.Line,
            EndCharacter = d.Range.End.Character,
            Source = d.Source,
            Uri = @params.Uri
        }).ToList();

        _diagnosticsByUri[@params.Uri] = list;
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanRequest(DocumentModel document, JsonElement? provider)
    {
        if (!_initialized || _rpc is null || document.Path is null) return false;
        if (provider is null) return false;
        return Capabilities?.Supports(provider) == true;
    }

    private async Task<HoverResult?> RequestPositionAsync(
        DocumentModel document,
        TextPosition position,
        string method,
        Func<JsonElement, HoverResult?> parse,
        CancellationToken ct)
    {
        if (!CanRequest(document, Capabilities?.HoverProvider)) return null;
        try
        {
            var raw = await _rpc!.InvokeWithParameterObjectAsync<JsonElement>(method, PositionParams(document, position), ct);
            return parse(raw);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Method} failed", method);
            return null;
        }
    }

    private async Task<IReadOnlyList<Location>> RequestLocationsAsync(
        DocumentModel document, TextPosition position, string method, JsonElement? provider, CancellationToken ct)
    {
        if (!CanRequest(document, provider)) return Array.Empty<Location>();
        try
        {
            var raw = await _rpc!.InvokeWithParameterObjectAsync<JsonElement>(method, PositionParams(document, position), ct);
            return ParseLocations(raw);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Method} failed", method);
            return Array.Empty<Location>();
        }
    }

    private static object PositionParams(DocumentModel document, TextPosition position) => new
    {
        textDocument = new { uri = ToUri(document.Path!) },
        position = new { line = position.Line, character = position.Column }
    };

    private static string ToUri(string path) => new Uri(path).AbsoluteUri;

    private static object BuildClientCapabilities() => new
    {
        textDocument = new
        {
            synchronization = new { didSave = true, dynamicRegistration = false },
            hover = new { contentFormat = new[] { "markdown", "plaintext" } },
            completion = new
            {
                completionItem = new
                {
                    snippetSupport = false,
                    documentationFormat = new[] { "markdown", "plaintext" }
                }
            },
            signatureHelp = new { },
            definition = new { linkSupport = true },
            typeDefinition = new { linkSupport = true },
            implementation = new { linkSupport = true },
            references = new { },
            documentHighlight = new { },
            documentSymbol = new { },
            codeAction = new { },
            codeLens = new { },
            formatting = new { },
            rangeFormatting = new { },
            rename = new { prepareSupport = true },
            foldingRange = new { },
            selectionRange = new { },
            publishDiagnostics = new { relatedInformation = true },
            semanticTokens = new
            {
                requests = new { full = true, range = true },
                tokenTypes = new[]
                {
                    "namespace", "type", "class", "enum", "interface", "struct", "typeParameter",
                    "parameter", "variable", "property", "enumMember", "event", "function", "method",
                    "macro", "keyword", "modifier", "comment", "string", "number", "regexp", "operator"
                },
                tokenModifiers = new[] { "declaration", "definition", "readonly", "static", "deprecated", "documentation" },
                formats = new[] { "relative" }
            },
            inlayHint = new { }
        },
        workspace = new
        {
            applyEdit = true,
            workspaceEdit = new { documentChanges = true },
            symbol = new { },
            executeCommand = new { }
        },
        window = new
        {
            workDoneProgress = true,
            showMessage = new { }
        }
    };

    private static HoverResult? ParseHover(JsonElement raw)
    {
        if (raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (!raw.TryGetProperty("contents", out var contents)) return null;
        var (text, kind) = MarkupParser.ParseContents(contents);
        return string.IsNullOrEmpty(text) ? null : new HoverResult { Contents = text, Kind = kind };
    }

    private static IReadOnlyList<CompletionItem> ParseCompletions(JsonElement raw)
    {
        var items = new List<CompletionItem>();
        var arr = raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("items", out var i) ? i : raw;
        if (arr.ValueKind != JsonValueKind.Array) return items;
        foreach (var el in arr.EnumerateArray())
        {
            var label = el.TryGetProperty("label", out var l) ? l.GetString() : null;
            if (label is null) continue;
            string? docs = null;
            if (el.TryGetProperty("documentation", out var d))
                docs = MarkupParser.ParseContents(d).Text;
            items.Add(new CompletionItem
            {
                Label = label,
                Detail = el.TryGetProperty("detail", out var det) ? det.GetString() : null,
                Documentation = docs,
                Kind = el.TryGetProperty("kind", out var k) ? k.GetInt32() : null,
                InsertText = el.TryGetProperty("insertText", out var ins) ? ins.GetString() : null,
                TextEdit = el.TryGetProperty("textEdit", out var te) ? te.Clone() : null
            });
        }
        return items;
    }

    private static IReadOnlyList<Location> ParseLocations(JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.Null) return Array.Empty<Location>();
        if (raw.ValueKind == JsonValueKind.Object)
        {
            if (raw.TryGetProperty("targetUri", out _))
            {
                var link = raw.Deserialize<LocationLink>();
                if (link is null) return Array.Empty<Location>();
                return new[]
                {
                    new Location
                    {
                        Uri = link.TargetUri,
                        Range = link.TargetSelectionRange ?? link.TargetRange
                    }
                };
            }
            var loc = raw.Deserialize<Location>();
            return loc is null ? Array.Empty<Location>() : new[] { loc };
        }

        if (raw.ValueKind != JsonValueKind.Array) return Array.Empty<Location>();
        var list = new List<Location>();
        foreach (var el in raw.EnumerateArray())
        {
            if (el.TryGetProperty("targetUri", out _))
            {
                var link = el.Deserialize<LocationLink>();
                if (link is null) continue;
                list.Add(new Location
                {
                    Uri = link.TargetUri,
                    Range = link.TargetSelectionRange ?? link.TargetRange
                });
            }
            else
            {
                var loc = el.Deserialize<Location>();
                if (loc is not null) list.Add(loc);
            }
        }
        return list;
    }
}

public static class MarkupParser
{
    public static (string Text, string Kind) ParseContents(JsonElement contents)
    {
        if (contents.ValueKind == JsonValueKind.String)
            return (contents.GetString() ?? "", "plaintext");

        if (contents.ValueKind == JsonValueKind.Object)
        {
            var kind = contents.TryGetProperty("kind", out var k) ? k.GetString() ?? "plaintext" : "plaintext";
            var value = contents.TryGetProperty("value", out var v) ? v.GetString() ?? "" : contents.ToString();
            return (value, kind);
        }

        if (contents.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var el in contents.EnumerateArray())
                parts.Add(ParseContents(el).Text);
            return (string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p))), "markdown");
        }

        return (contents.ToString(), "plaintext");
    }
}
