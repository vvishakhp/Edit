using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Dock.Avalonia.Themes.Fluent;
using Edit.App;
using Edit.ComponentModel;
using Edit.Core;
using Edit.Lsp;
using Edit.Platform;
using Edit.Plugins.Files;
using Edit.Plugins.Git;
using Edit.Plugins.Host;
using Edit.Plugins.Sample;
using Edit.Plugins.Search;
using Edit.Plugins.Terminal;
using Edit.Dap;
using Edit.Shell;
using Edit.TreeSitter;
using Edit.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Edit.App;

public sealed class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        // Code-only app resources (no XAML App.axaml required)
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = Services.GetRequiredService<ShellViewModel>();
            var pluginHost = Services.GetRequiredService<PluginHost>();
            var context = Services.GetRequiredService<IPluginContext>();
            _ = pluginHost.LoadInProcessAsync(new IPlugin[]
            {
                new FilesPlugin(),
                new SampleToolPlugin(),
                new SearchPlugin(),
                new GitPlugin(),
                new TerminalPlugin()
            }, context);

            // Attach sample tool if registered
            var tools = Services.GetRequiredService<IToolWindowRegistry>();
            foreach (var tool in tools.All)
            {
                if (shell.ToolContents.Any(t => t.Id == tool.Id)) continue;
                var content = tool.CreateContent(Services);
                if (content is Avalonia.Controls.Control control)
                    shell.ToolContents.Add(new ToolDockable(tool.Id, tool.Title, control));
            }

            desktop.MainWindow = new MainWindow(shell);
            ApplySettings(shell);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(b => b.AddConsole());
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<IToolWindowRegistry, ToolWindowRegistry>();
        services.AddSingleton<DocumentService>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<LanguageClient>();
        services.AddSingleton<MockLanguageClient>();
        services.AddSingleton<ISyntaxColorTheme>(_ =>
        {
            var path = AppPaths.SyntaxColorsPath;
            return File.Exists(path) ? SyntaxColorTheme.LoadFromFile(path) : SyntaxColorTheme.CreateDefault();
        });
        services.AddSingleton<ISyntaxHighlighter>(_ => TreeSitterHighlighter.CreateDefault());
        services.AddSingleton<PluginHost>();
        services.AddSingleton<IExtensionHost, NoOpExtensionHost>();
        services.AddSingleton<IDebugAdapterClient, NoOpDebugAdapterClient>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<GitService>();
        services.AddSingleton<TerminalSession>();
        services.AddSingleton<IPluginContext>(sp =>
            new DefaultPluginContext(
                sp,
                sp.GetRequiredService<ICommandRegistry>(),
                sp.GetRequiredService<IToolWindowRegistry>()));
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<SettingsStore>();
    }

    private static void ApplySettings(ShellViewModel shell)
    {
        var store = Services.GetRequiredService<SettingsStore>();
        var settings = store.Load();
        if (settings.Keybindings.Count == 0)
            settings.Keybindings = KeybindingDefaults.Create();
        shell.Log($"Loaded settings schema v{settings.SchemaVersion} ({settings.LanguageServers.Count} language servers, {settings.Keybindings.Count} keybindings)");
        _ = Services.GetRequiredService<IExtensionHost>().StartAsync();
        _ = Services.GetRequiredService<IDebugAdapterClient>();
    }
}

public sealed class SettingsStore
{
    public SettingsModel Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var json = File.ReadAllText(AppPaths.SettingsPath);
                return JsonSerializer.Deserialize<SettingsModel>(json) ?? new SettingsModel();
            }
        }
        catch
        {
            // ignore
        }

        var model = new SettingsModel
        {
            SchemaVersion = 1,
            LanguageServers =
            {
                new LanguageServerSettings
                {
                    Id = "example",
                    Command = "",
                    FileGlobs = { "**/*" }
                }
            },
            Keybindings = KeybindingDefaults.Create()
        };
        Save(model);
        return model;
    }

    public void Save(SettingsModel model)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsPath)!);
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
