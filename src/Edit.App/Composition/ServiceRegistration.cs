using Edit.App.Settings;
using Edit.ComponentModel;
using Edit.Core;
using Edit.Dap;
using Edit.Lsp;
using Edit.Platform;
using Edit.Plugins.Git;
using Edit.Plugins.Host;
using Edit.Plugins.Search;
using Edit.Plugins.Terminal;
using Edit.Shell;
using Edit.TreeSitter;
using Edit.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edit.App.Composition;

/// <summary>Central DI registration for the Edit application host.</summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddEditServices(this IServiceCollection services)
    {
        services.AddLogging(b => b.AddConsole());

        RegisterCore(services);
        RegisterLanguageServices(services);
        RegisterPluginsAndExtensions(services);
        RegisterWorkbench(services);

        return services;
    }

    private static void RegisterCore(IServiceCollection services)
    {
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<IToolWindowRegistry, ToolWindowRegistry>();
        services.AddSingleton<DocumentService>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<ISyntaxColorTheme>(_ =>
        {
            var path = AppPaths.SyntaxColorsPath;
            return File.Exists(path)
                ? SyntaxColorTheme.LoadFromFile(path)
                : SyntaxColorTheme.CreateDefault();
        });
        services.AddSingleton<ISyntaxHighlighter>(_ => TreeSitterHighlighter.CreateDefault());
    }

    private static void RegisterLanguageServices(IServiceCollection services)
    {
        services.AddSingleton<ILspContributionRegistry, LspContributionRegistry>();
        services.AddSingleton<ILanguageServerRegistry, LanguageServerRegistry>();
        services.AddSingleton<ILanguageClientManager, LanguageClientManager>();
        services.AddSingleton<LanguageClient>();
        services.AddSingleton<MockLanguageClient>();
        services.AddSingleton<IDebugAdapterClient, DebugAdapterClient>();
    }

    private static void RegisterPluginsAndExtensions(IServiceCollection services)
    {
        services.AddSingleton<PluginHost>();
        services.AddSingleton<IExtensionHost, JsonRpcExtensionHost>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<GitService>();
        services.AddSingleton<TerminalSession>();
        services.AddSingleton<IPluginContext>(sp =>
            new DefaultPluginContext(
                sp,
                sp.GetRequiredService<ICommandRegistry>(),
                sp.GetRequiredService<IToolWindowRegistry>(),
                sp.GetRequiredService<ILspContributionRegistry>()));
    }

    private static void RegisterWorkbench(IServiceCollection services)
    {
        services.AddSingleton<ShellViewModel>();
    }
}
