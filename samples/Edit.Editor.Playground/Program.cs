using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Edit.Core;
using Edit.Editor;
using Edit.TreeSitter;

namespace Edit.Editor.Playground;

public class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var docs = new DocumentService();
            var doc = docs.CreateUntitled("public class Demo {\n    // type here\n}\n");
            doc.GrammarId = "csharp";
            desktop.MainWindow = new Window
            {
                Title = "Edit Editor Playground",
                Width = 900,
                Height = 600,
                Content = new CodeEditorControl
                {
                    Document = doc,
                    Highlighter = TreeSitterHighlighter.CreateDefault(),
                    SyntaxTheme = SyntaxColorTheme.CreateDefault()
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
