using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Edit.ComponentModel;
using Edit.Core;
using Edit.Lsp;
using Edit.Shell;
using Edit.TreeSitter;
using Edit.Workspace;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Edit.UI.Tests;

public class ShellUiTests
{
    private static ShellViewModel CreateShell()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<DocumentService>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<LanguageClient>();
        services.AddSingleton<MockLanguageClient>();
        services.AddSingleton<ISyntaxHighlighter>(_ => TreeSitterHighlighter.CreateDefault());
        services.AddSingleton<ISyntaxColorTheme>(_ => SyntaxColorTheme.CreateDefault());
        var sp = services.BuildServiceProvider();
        return new ShellViewModel(
            sp.GetRequiredService<DocumentService>(),
            sp.GetRequiredService<WorkspaceService>(),
            sp.GetRequiredService<ISyntaxHighlighter>(),
            sp.GetRequiredService<ISyntaxColorTheme>(),
            sp.GetRequiredService<ICommandRegistry>(),
            sp.GetRequiredService<LanguageClient>(),
            sp.GetRequiredService<MockLanguageClient>());
    }

    [AvaloniaFact]
    public void Status_bar_control_exists_on_main_window()
    {
        var shell = CreateShell();
        var window = new MainWindow(shell);
        window.Content.Should().NotBeNull();
        FindByName(window, "StatusBar").Should().NotBeNull();
        FindByName(window, "RootDock").Should().NotBeNull();
        FindByName(window, "StatusLineColumn").Should().NotBeNull();
        FindByName(window, "MainMenu").Should().NotBeNull();
    }

    [AvaloniaFact]
    public void Editor_accepts_text_input_and_updates_caret()
    {
        var shell = CreateShell();
        var doc = shell.DocumentsService.CreateUntitled("");
        var editor = shell.OpenDocument(doc).Editor;
        editor.Width = 400;
        editor.Height = 300;

        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = editor,
            Background = Brushes.Black
        };
        window.Show();
        editor.Focus();
        window.KeyTextInput("abc");

        doc.Buffer.GetText().Should().Be("abc");
        doc.CaretOffset.Should().Be(3);
    }

    [AvaloniaFact]
    public void Status_bar_reflects_line_column()
    {
        var shell = CreateShell();
        var doc = shell.DocumentsService.CreateUntitled("hi");
        doc.CaretOffset = 2;
        shell.OpenDocument(doc);
        shell.RefreshStatus();
        shell.StatusBar.LineColumn.Should().Contain("Col 3");
    }

    [AvaloniaFact]
    public void Enter_applies_auto_indent_after_brace()
    {
        var shell = CreateShell();
        var doc = shell.DocumentsService.CreateUntitled("void M() {");
        doc.GrammarId = "csharp";
        doc.CaretOffset = doc.Buffer.Length;
        var editor = shell.OpenDocument(doc).Editor;
        var indent = shell.Highlighter.ComputeIndentOnEnter(doc.CaretOffset);
        var insert = "\n" + new string(' ', indent);
        doc.Buffer.ApplyEdit(doc.CaretOffset, 0, insert);
        doc.CaretOffset += insert.Length;

        doc.Buffer.GetLineContent(1).Should().StartWith("    ");
        editor.Should().NotBeNull();
    }

    private static Control? FindByName(Control root, string name)
    {
        if (root.Name == name) return root;
        foreach (var child in root.GetLogicalChildren().OfType<Control>())
        {
            var found = FindByName(child, name);
            if (found is not null) return found;
        }
        return null;
    }
}
