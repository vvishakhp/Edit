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

/// <summary>Stub for future out-of-process extension host over JSON-RPC.</summary>
public interface IExtensionHost
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class NoOpExtensionHost : IExtensionHost
{
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
