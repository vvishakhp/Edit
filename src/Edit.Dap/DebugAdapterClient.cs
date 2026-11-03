using System.Diagnostics;
using System.Text.Json;
using Edit.Platform;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Edit.Dap;

/// <summary>Debug Adapter Protocol client surface.</summary>
public interface IDebugAdapterClient
{
    string Status { get; }
    Task StartAsync(DebugAdapterLaunchRequest request, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task ContinueAsync(CancellationToken cancellationToken = default);
    Task SetBreakpointsAsync(string path, IReadOnlyList<int> lines, CancellationToken cancellationToken = default);
}

public sealed class DebugAdapterLaunchRequest
{
    public required string AdapterCommand { get; init; }
    public List<string> Args { get; init; } = new();
    public string? WorkingDirectory { get; init; }
    public string? Program { get; init; }
    public string Transport { get; init; } = "stdio";
    public string? Host { get; init; }
    public int? Port { get; init; }
}

public sealed class NoOpDebugAdapterClient : IDebugAdapterClient
{
    public string Status => "DAP: Off";
    public Task StartAsync(DebugAdapterLaunchRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetBreakpointsAsync(string path, IReadOnlyList<int> lines, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>DAP client with stdio/socket transport and initialize handshake.</summary>
public sealed class DebugAdapterClient : IDebugAdapterClient, IAsyncDisposable
{
    private readonly ILogger<DebugAdapterClient> _logger;
    private Process? _process;
    private JsonRpc? _rpc;
    private System.Net.Sockets.TcpClient? _tcp;

    public DebugAdapterClient(ILogger<DebugAdapterClient> logger)
    {
        _logger = logger;
        TryLoadDefaultFromConfig();
    }

    public string Status { get; private set; } = "DAP: Off";

    public async Task StartAsync(DebugAdapterLaunchRequest request, CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.AdapterCommand))
        {
            Status = "DAP: Off";
            return;
        }

        try
        {
            Stream sending;
            Stream receiving;
            if (string.Equals(request.Transport, "socket", StringComparison.OrdinalIgnoreCase))
            {
                _tcp = new System.Net.Sockets.TcpClient();
                await _tcp.ConnectAsync(request.Host ?? "127.0.0.1", request.Port ?? 4711, cancellationToken);
                var stream = _tcp.GetStream();
                sending = stream;
                receiving = stream;
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = request.AdapterCommand,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory
                };
                foreach (var arg in request.Args)
                    psi.ArgumentList.Add(arg);
                _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start debug adapter");
                sending = _process.StandardInput.BaseStream;
                receiving = _process.StandardOutput.BaseStream;
            }

            var formatter = new SystemTextJsonFormatter();
            var handler = new HeaderDelimitedMessageHandler(sending, receiving, formatter);
            _rpc = new JsonRpc(handler);
            _rpc.StartListening();

            await _rpc.InvokeWithParameterObjectAsync("initialize", new
            {
                clientID = "edit",
                clientName = "Edit",
                adapterID = "edit",
                pathFormat = "path",
                linesStartAt1 = true,
                columnsStartAt1 = true
            }, cancellationToken);

            await _rpc.NotifyWithParameterObjectAsync("initialized", new { });
            Status = $"DAP: {Path.GetFileName(request.AdapterCommand)}";
            _logger.LogInformation("Debug adapter started: {Command}", request.AdapterCommand);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start debug adapter");
            Status = "DAP: Error";
            await StopAsync(cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_rpc is not null)
                await _rpc.NotifyAsync("disconnect", new { terminateDebuggee = true });
        }
        catch
        {
            // ignore
        }

        _rpc?.Dispose();
        _rpc = null;
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
        _process?.Dispose();
        _process = null;
        _tcp?.Dispose();
        _tcp = null;
        Status = "DAP: Off";
    }

    public async Task ContinueAsync(CancellationToken cancellationToken = default)
    {
        if (_rpc is null) return;
        await _rpc.InvokeWithParameterObjectAsync("continue", new { threadId = 1 }, cancellationToken);
    }

    public async Task SetBreakpointsAsync(string path, IReadOnlyList<int> lines, CancellationToken cancellationToken = default)
    {
        if (_rpc is null) return;
        await _rpc.InvokeWithParameterObjectAsync("setBreakpoints", new
        {
            source = new { path },
            breakpoints = lines.Select(l => new { line = l }).ToArray()
        }, cancellationToken);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private void TryLoadDefaultFromConfig()
    {
        try
        {
            var path = AppPaths.DebugAdaptersPath;
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("adapters", out var adapters) || adapters.GetArrayLength() == 0)
                return;
            var first = adapters[0];
            var command = first.TryGetProperty("command", out var c) ? c.GetString() : null;
            if (!string.IsNullOrWhiteSpace(command))
                _logger.LogDebug("DAP config available ({Command}); start via IDebugAdapterClient.StartAsync", command);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read debug-adapters.json");
        }
    }
}
