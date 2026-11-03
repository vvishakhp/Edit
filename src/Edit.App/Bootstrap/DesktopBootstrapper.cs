using Avalonia.Controls.ApplicationLifetimes;
using Edit.App.Settings;
using Edit.ComponentModel;
using Edit.Core;
using Edit.Dap;
using Edit.Lsp;
using Edit.Plugins.Host;
using Edit.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Edit.App.Bootstrap;

/// <summary>Wires the desktop lifetime: plugins, tool windows, main window, and settings.</summary>
public sealed class DesktopBootstrapper
{
    private readonly IServiceProvider _services;

    public DesktopBootstrapper(IServiceProvider services)
    {
        _services = services;
    }

    public void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var shell = _services.GetRequiredService<ShellViewModel>();
        LoadFirstPartyPlugins();
        AttachRegisteredToolWindows(shell);

        desktop.MainWindow = new MainWindow(shell);
        ApplyStartupSettings(shell);
    }

    private void LoadFirstPartyPlugins()
    {
        var pluginHost = _services.GetRequiredService<PluginHost>();
        var context = _services.GetRequiredService<IPluginContext>();
        _ = pluginHost.LoadInProcessAsync(FirstPartyPluginCatalog.Create(), context);
    }

    private void AttachRegisteredToolWindows(ShellViewModel shell)
    {
        var tools = _services.GetRequiredService<IToolWindowRegistry>();
        foreach (var tool in tools.All)
        {
            if (shell.ToolContents.Any(t => t.Id == tool.Id))
                continue;

            var content = tool.CreateContent(_services);
            if (content is Avalonia.Controls.Control control)
                shell.ToolContents.Add(new ToolDockable(tool.Id, tool.Title, control));
        }
    }

    private void ApplyStartupSettings(ShellViewModel shell)
    {
        var store = _services.GetRequiredService<SettingsStore>();
        var settings = store.Load();
        if (settings.Keybindings.Count == 0)
            settings.Keybindings = KeybindingDefaults.Create();

        var registry = _services.GetRequiredService<ILanguageServerRegistry>();
        shell.Log(
            $"Loaded settings schema v{settings.SchemaVersion} " +
            $"({registry.Servers.Count} language servers from config, {settings.Keybindings.Count} keybindings)");

        _ = _services.GetRequiredService<IExtensionHost>().StartAsync();
        _ = _services.GetRequiredService<IDebugAdapterClient>();
    }
}
