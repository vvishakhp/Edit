using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Dock.Avalonia.Themes.Fluent;
using Edit.App.Bootstrap;
using Edit.App.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Edit.App;

/// <summary>Avalonia application shell — theming and desktop bootstrap only.</summary>
public sealed class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddEditServices();
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            new DesktopBootstrapper(Services).Start(desktop);

        base.OnFrameworkInitializationCompleted();
    }
}
