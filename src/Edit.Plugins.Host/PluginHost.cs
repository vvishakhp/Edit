using System.Reflection;
using System.Runtime.Loader;
using Edit.ComponentModel;
using Edit.Core;
using Microsoft.Extensions.Logging;

namespace Edit.Plugins.Host;

public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}

public sealed class PluginHost
{
    private readonly ILogger<PluginHost> _logger;
    private readonly List<IPlugin> _plugins = new();
    private readonly List<PluginLoadContext> _contexts = new();

    public PluginHost(ILogger<PluginHost> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<IPlugin> Plugins => _plugins;

    public async Task LoadFromDirectoryAsync(string directory, IPluginContext context, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogInformation("Plugin directory missing: {Dir}", directory);
            return;
        }

        foreach (var dll in Directory.GetFiles(directory, "Edit.Plugins.*.dll", SearchOption.AllDirectories))
        {
            if (dll.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                dll.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                await LoadAssemblyAsync(dll, context, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plugin {Dll}", dll);
            }
        }
    }

    public async Task LoadAssemblyAsync(string assemblyPath, IPluginContext context, CancellationToken cancellationToken = default)
    {
        var alc = new PluginLoadContext(assemblyPath);
        _contexts.Add(alc);
        var assembly = alc.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        foreach (var type in assembly.GetTypes())
        {
            if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract) continue;
            if (Activator.CreateInstance(type) is not IPlugin plugin) continue;
            await plugin.InitializeAsync(context, cancellationToken);
            _plugins.Add(plugin);
            _logger.LogInformation("Loaded plugin {Id} ({Name})", plugin.Id, plugin.Name);
        }
    }

    /// <summary>Load plugins that are already referenced by the host (first-party).</summary>
    public async Task LoadInProcessAsync(IEnumerable<IPlugin> plugins, IPluginContext context, CancellationToken cancellationToken = default)
    {
        foreach (var plugin in plugins)
        {
            await plugin.InitializeAsync(context, cancellationToken);
            _plugins.Add(plugin);
            _logger.LogInformation("Loaded in-process plugin {Id}", plugin.Id);
        }
    }
}

/// <summary>Out-of-process extension host skeleton over JSON-RPC (stdio child process when configured).</summary>
public interface IExtensionHost
{
    string Status { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class NoOpExtensionHost : IExtensionHost
{
    public string Status => "ExtHost: Off";
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// JSON-RPC extension host that is ready to attach to a child process.
/// Currently runs in-process and exposes status; process launch is activated when EDIT_EXTENSION_HOST is set.
/// </summary>
public sealed class JsonRpcExtensionHost : IExtensionHost, IAsyncDisposable
{
    private readonly ILogger<JsonRpcExtensionHost> _logger;
    private System.Diagnostics.Process? _process;
    private StreamJsonRpc.JsonRpc? _rpc;

    public JsonRpcExtensionHost(ILogger<JsonRpcExtensionHost> logger)
    {
        _logger = logger;
    }

    public string Status { get; private set; } = "ExtHost: InProc";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var command = Environment.GetEnvironmentVariable("EDIT_EXTENSION_HOST");
        if (string.IsNullOrWhiteSpace(command))
        {
            Status = "ExtHost: InProc";
            _logger.LogInformation("Extension host running in-process (set EDIT_EXTENSION_HOST to launch OOP host)");
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _process = System.Diagnostics.Process.Start(psi);
            if (_process is null)
            {
                Status = "ExtHost: Failed";
                return;
            }

            var formatter = new StreamJsonRpc.SystemTextJsonFormatter();
            var handler = new StreamJsonRpc.HeaderDelimitedMessageHandler(
                _process.StandardInput.BaseStream,
                _process.StandardOutput.BaseStream,
                formatter);
            _rpc = new StreamJsonRpc.JsonRpc(handler);
            _rpc.StartListening();
            await _rpc.InvokeAsync("initialize", cancellationToken);
            Status = "ExtHost: OOP";
            _logger.LogInformation("Out-of-process extension host started: {Command}", command);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start OOP extension host");
            Status = "ExtHost: Error";
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_rpc is not null)
                await _rpc.NotifyAsync("exit");
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
        Status = "ExtHost: Off";
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
