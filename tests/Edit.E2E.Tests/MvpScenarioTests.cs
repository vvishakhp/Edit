using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Edit.ComponentModel;
using Edit.Core;
using Edit.Lsp;
using Edit.Shell;
using Edit.TreeSitter;
using Edit.Workspace;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Edit.E2E.Tests;

public class MvpScenarioTests
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

    private static string FindFixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "sample-workspace");
            if (Directory.Exists(candidate)) return candidate;
            var copied = Path.Combine(dir.FullName, "fixtures", "sample-workspace");
            if (Directory.Exists(copied)) return copied;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("sample-workspace fixture not found");
    }

    [AvaloniaFact]
    public void Cold_start_builds_default_chrome()
    {
        var shell = CreateShell();
        var window = new MainWindow(shell);
        FindByName(window, "StatusBar").Should().NotBeNull();
        FindByName(window, "RootDock").Should().NotBeNull();
        FindByName(window, "MainMenu").Should().NotBeNull();
        shell.Layout.Should().NotBeNull();
        shell.ToolContents.Should().Contain(t => t.Id == "explorer");
        shell.ToolContents.Should().Contain(t => t.Id == "problems");
        shell.ToolContents.Should().Contain(t => t.Id == "output");
    }

    [AvaloniaFact]
    public void Open_folder_populates_explorer()
    {
        var shell = CreateShell();
        var fixture = FindFixture();
        shell.Workspace.OpenFolder(fixture);
        shell.RefreshExplorer();
        shell.ExplorerNodes.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Open_edit_save_roundtrip()
    {
        var shell = CreateShell();
        var fixture = FindFixture();
        var file = Path.Combine(fixture, "hello.cs");
        var doc = shell.DocumentsService.OpenFile(file);
        shell.OpenDocument(doc);
        doc.GrammarId.Should().Be("csharp");

        var original = doc.Buffer.GetText();
        doc.Buffer.ApplyEdit(doc.Buffer.Length, 0, "\n// edited");
        var temp = Path.Combine(Path.GetTempPath(), $"edit-e2e-{Guid.NewGuid():N}.cs");
        shell.DocumentsService.Save(doc, temp);
        File.ReadAllText(temp).Should().Contain("// edited");
        File.ReadAllText(temp).Should().StartWith(original.Split('\n')[0]);
    }

    [AvaloniaFact]
    public void Tree_sitter_native_highlights_open_file()
    {
        var root = FindNativesRoot();
        if (root is null || !Directory.Exists(Path.Combine(root, "csharp")))
            return; // natives not built in this environment

        var shell = CreateShell();
        var file = Path.Combine(FindFixture(), "hello.cs");
        var doc = shell.DocumentsService.OpenFile(file);
        shell.OpenDocument(doc);
        shell.Highlighter.UpdateDocument(doc.Buffer.CreateSnapshot(), doc.GrammarId);
        shell.Highlighter.UsesNativeTreeSitter.Should().BeTrue();
        shell.Highlighter.GetHighlights(0, doc.Buffer.Length).Should().NotBeEmpty();
    }

    private static string? FindNativesRoot()
    {
        var root = Edit.Platform.AppPaths.TreeSitterNativesRoot();
        return Directory.Exists(root) ? root : null;
    }

    [AvaloniaFact]
    public void Layout_save_writes_schema_version()
    {
        var temp = Path.Combine(Path.GetTempPath(), "edit-e2e-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("EDIT_USER_DATA", temp);
        try
        {
            var shell = CreateShell();
            shell.SaveLayout();
            File.Exists(Edit.Platform.AppPaths.LayoutPath).Should().BeTrue();
            var json = File.ReadAllText(Edit.Platform.AppPaths.LayoutPath);
            json.Should().Contain("SchemaVersion");
        }
        finally
        {
            Environment.SetEnvironmentVariable("EDIT_USER_DATA", null);
            try { Directory.Delete(temp, true); } catch { /* ignore */ }
        }
    }

    [AvaloniaFact]
    public async Task Commands_undo_and_new_file()
    {
        var shell = CreateShell();
        await shell.Commands.ExecuteAsync("file.new");
        shell.Documents.Should().NotBeEmpty();
        var doc = shell.DocumentsService.ActiveDocument!;
        doc.Buffer.ApplyEdit(0, 0, "x");
        await shell.Commands.ExecuteAsync("edit.undo");
        doc.Buffer.GetText().Should().NotContain("x");
    }

    [AvaloniaFact]
    public void Mock_lsp_diagnostics_show_in_problems()
    {
        var shell = CreateShell();
        shell.MockLsp.PublishSampleDiagnostic(0, "e2e warning");
        shell.RefreshProblemsFromLsp();
        shell.Problems.Should().Contain(p => p.Contains("e2e warning"));
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
