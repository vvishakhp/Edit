using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Edit.Core;
using Edit.Text;
using Microsoft.Extensions.Logging;

namespace Edit.Lsp;

public sealed class DiagnosticItem
{
    public required string Message { get; init; }
    public required string Severity { get; init; }
    public required int Line { get; init; }
    public required int Character { get; init; }
    public string? Source { get; init; }
}

public sealed class HoverResult
{
    public required string Contents { get; init; }
}

public sealed class CompletionItem
{
    public required string Label { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// Minimal JSON-RPC LSP client over stdio. Settings-driven; no bundled servers.
/// </summary>
public sealed class LanguageClient : IAsyncDisposable
{
    private readonly ILogger<LanguageClient> _logger;
    private Process? _process;
    private StreamWriter? _stdin;
    private CancellationTokenSource? _cts;
    private int _nextId = 1;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly List<DiagnosticItem> _diagnostics = new();
    private string? _rootUri;
    private bool _initialized;

    public LanguageClient(ILogger<LanguageClient> logger)
    {
        _logger = logger;
    }

    public string Status { get; private set; } = "LSP: Off";
    public IReadOnlyList<DiagnosticItem> Diagnostics => _diagnostics;
    public event EventHandler? DiagnosticsChanged;

    public async Task StartAsync(LanguageServerSettings settings, string workspaceRoot, CancellationToken cancellationToken = default)
    {
        await StopAsync();
        if (string.IsNullOrWhiteSpace(settings.Command))
        {
            Status = "LSP: Off";
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = settings.Command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workspaceRoot
            };
            foreach (var arg in settings.Args)
                psi.ArgumentList.Add(arg);

            _process = Process.Start(psi);
            if (_process is null)
            {
                Status = "LSP: Failed";
                return;
            }

            _stdin = _process.StandardInput;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
            _rootUri = new Uri(workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
                ? workspaceRoot
                : workspaceRoot + Path.DirectorySeparatorChar).AbsoluteUri;

            await SendRequestAsync("initialize", new
            {
                processId = Environment.ProcessId,
                rootUri = _rootUri,
                capabilities = new
                {
                    textDocument = new
                    {
                        synchronization = new { didSave = true },
                        hover = new { contentFormat = new[] { "plaintext", "markdown" } },
                        completion = new { },
                        publishDiagnostics = new { }
                    }
                },
                clientInfo = new { name = "Edit", version = "0.1.0" }
            }, cancellationToken);

            await SendNotificationAsync("initialized", new { });
            _initialized = true;
            Status = $"LSP: {settings.Id}";
            _logger.LogInformation("Language server {Id} started", settings.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start language server {Command}", settings.Command);
            Status = "LSP: Error";
        }
    }

    public async Task DidOpenAsync(DocumentModel document)
    {
        if (!_initialized || document.Path is null) return;
        var uri = new Uri(document.Path).AbsoluteUri;
        await SendNotificationAsync("textDocument/didOpen", new
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
        if (!_initialized || document.Path is null) return;
        var uri = new Uri(document.Path).AbsoluteUri;
        await SendNotificationAsync("textDocument/didChange", new
        {
            textDocument = new { uri, version = document.Buffer.Version },
            contentChanges = new[] { new { text = document.Buffer.GetText() } }
        });
    }

    public async Task<HoverResult?> HoverAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default)
    {
        if (!_initialized || document.Path is null) return null;
        try
        {
            var result = await SendRequestAsync("textDocument/hover", new
            {
                textDocument = new { uri = new Uri(document.Path).AbsoluteUri },
                position = new { line = position.Line, character = position.Column }
            }, cancellationToken);

            if (result.ValueKind == JsonValueKind.Null) return null;
            if (result.TryGetProperty("contents", out var contents))
            {
                var text = contents.ValueKind == JsonValueKind.String
                    ? contents.GetString()
                    : contents.ToString();
                return new HoverResult { Contents = text ?? string.Empty };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Hover failed");
        }

        return null;
    }

    public async Task<IReadOnlyList<CompletionItem>> CompleteAsync(DocumentModel document, TextPosition position, CancellationToken cancellationToken = default)
    {
        if (!_initialized || document.Path is null) return Array.Empty<CompletionItem>();
        try
        {
            var result = await SendRequestAsync("textDocument/completion", new
            {
                textDocument = new { uri = new Uri(document.Path).AbsoluteUri },
                position = new { line = position.Line, character = position.Column }
            }, cancellationToken);

            var items = new List<CompletionItem>();
            var arr = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("items", out var i) ? i : result;
            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var label = el.TryGetProperty("label", out var l) ? l.GetString() : null;
                    if (label is null) continue;
                    items.Add(new CompletionItem
                    {
                        Label = label,
                        Detail = el.TryGetProperty("detail", out var d) ? d.GetString() : null
                    });
                }
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Completion failed");
            return Array.Empty<CompletionItem>();
        }
    }

    public async Task StopAsync()
    {
        try
        {
            if (_initialized)
                await SendNotificationAsync("shutdown", null!);
        }
        catch { /* ignore */ }

        _cts?.Cancel();
        _stdin?.Dispose();
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }
        _process?.Dispose();
        _process = null;
        _initialized = false;
        Status = "LSP: Off";
        _diagnostics.Clear();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task SendNotificationAsync(string method, object? @params)
    {
        if (_stdin is null) return;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = @params
        });
        await WriteMessageAsync(payload);
    }

    private async Task<JsonElement> SendRequestAsync(string method, object? @params, CancellationToken cancellationToken)
    {
        if (_stdin is null) throw new InvalidOperationException("LSP not started");
        var id = _nextId++;
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params
        });
        await WriteMessageAsync(payload);
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task;
    }

    private async Task WriteMessageAsync(string payload)
    {
        if (_stdin is null) return;
        var bytes = Encoding.UTF8.GetByteCount(payload);
        await _stdin.WriteAsync($"Content-Length: {bytes}\r\n\r\n");
        await _stdin.WriteAsync(payload);
        await _stdin.FlushAsync();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_process is null) return;
        var stream = _process.StandardOutput.BaseStream;
        var headerBuffer = new MemoryStream();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var headers = await ReadHeadersAsync(stream, headerBuffer, cancellationToken);
                if (headers is null) break;
                if (!headers.TryGetValue("Content-Length", out var lenStr) || !int.TryParse(lenStr, out var len))
                    continue;
                var body = new byte[len];
                var read = 0;
                while (read < len)
                {
                    var n = await stream.ReadAsync(body.AsMemory(read, len - read), cancellationToken);
                    if (n == 0) return;
                    read += n;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement.Clone();
                if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id) &&
                    _pending.Remove(id, out var tcs))
                {
                    if (root.TryGetProperty("result", out var result))
                        tcs.TrySetResult(result.Clone());
                    else if (root.TryGetProperty("error", out var error))
                        tcs.TrySetException(new InvalidOperationException(error.ToString()));
                    else
                        tcs.TrySetResult(default);
                }
                else if (root.TryGetProperty("method", out var method) &&
                         method.GetString() == "textDocument/publishDiagnostics")
                {
                    HandleDiagnostics(root.GetProperty("params"));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "LSP read loop error");
            }
        }
    }

    private void HandleDiagnostics(JsonElement @params)
    {
        _diagnostics.Clear();
        if (!@params.TryGetProperty("diagnostics", out var diags)) return;
        foreach (var d in diags.EnumerateArray())
        {
            var msg = d.GetProperty("message").GetString() ?? "";
            var sev = d.TryGetProperty("severity", out var s) ? s.GetInt32() switch
            {
                1 => "Error",
                2 => "Warning",
                3 => "Info",
                _ => "Hint"
            } : "Info";
            var line = d.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32();
            var ch = d.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32();
            _diagnostics.Add(new DiagnosticItem
            {
                Message = msg,
                Severity = sev,
                Line = line,
                Character = ch,
                Source = d.TryGetProperty("source", out var src) ? src.GetString() : null
            });
        }
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<Dictionary<string, string>?> ReadHeadersAsync(Stream stream, MemoryStream buffer, CancellationToken ct)
    {
        buffer.SetLength(0);
        var b = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(b, ct);
            if (n == 0) return null;
            buffer.WriteByte(b[0]);
            if (buffer.Length >= 4)
            {
                var arr = buffer.ToArray();
                if (arr[^4] == '\r' && arr[^3] == '\n' && arr[^2] == '\r' && arr[^1] == '\n')
                {
                    var text = Encoding.ASCII.GetString(arr[..^4]);
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                    {
                        var idx = line.IndexOf(':');
                        if (idx > 0)
                            map[line[..idx].Trim()] = line[(idx + 1)..].Trim();
                    }
                    return map;
                }
            }
        }
    }
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
        return new HoverResult { Contents = string.IsNullOrEmpty(text) ? "(empty)" : $"mock hover: {text}" };
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
            Message = message,
            Severity = "Warning",
            Source = "mock"
        });
    }
}
