using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Edit.ComponentModel;
using Edit.Core;
using Edit.Lsp;
using Edit.Platform;
using Edit.TreeSitter;
using Edit.Workspace;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading;

namespace Edit.Shell;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly DocumentService _documents;
    private readonly WorkspaceService _workspace;
    private readonly ISyntaxHighlighter _highlighter;
    private readonly ISyntaxColorTheme _syntaxTheme;
    private readonly ICommandRegistry _commands;
    private readonly LanguageClient _lsp;
    private readonly MockLanguageClient _mockLsp;
    private readonly StatusBarModel _statusBar = new();
    private CancellationTokenSource? _explorerRefreshDebounce;

    public ShellViewModel(
        DocumentService documents,
        WorkspaceService workspace,
        ISyntaxHighlighter highlighter,
        ISyntaxColorTheme syntaxTheme,
        ICommandRegistry commands,
        LanguageClient lsp,
        MockLanguageClient mockLsp)
    {
        _documents = documents;
        _workspace = workspace;
        _highlighter = highlighter;
        _syntaxTheme = syntaxTheme;
        _commands = commands;
        _lsp = lsp;
        _mockLsp = mockLsp;

        Documents = new ObservableCollection<DocumentDockable>();
        ToolContents = new ObservableCollection<ToolDockable>();

        Factory = new ShellDockFactory(this);
        Layout = Factory.CreateLayout();
        if (Layout is not null)
            Factory.InitLayout(Layout);

        RegisterBuiltinCommands();
        _documents.ActiveDocumentChanged += (_, _) =>
        {
            RefreshStatus();
            UpdateWindowTitle();
        };
        _workspace.Changed += (_, _) =>
        {
            ScheduleRefreshExplorer();
            UiDispatcher.Invoke(UpdateWindowTitle);
        };
        _lsp.DiagnosticsChanged += (_, _) => RefreshProblemsFromLsp();
        UpdateWindowTitle();
    }

    [ObservableProperty] private string _windowTitle = "Edit";
    [ObservableProperty] private ObservableCollection<FileNode> _explorerNodes = new();
    [ObservableProperty] private ObservableCollection<string> _problems = new();
    [ObservableProperty] private ObservableCollection<string> _outputLines = new();

    public ShellDockFactory Factory { get; }
    public IRootDock? Layout { get; set; }
    public ObservableCollection<DocumentDockable> Documents { get; }
    public ObservableCollection<ToolDockable> ToolContents { get; }
    public StatusBarModel StatusBar => _statusBar;
    public DocumentService DocumentsService => _documents;
    public WorkspaceService Workspace => _workspace;
    public ICommandRegistry Commands => _commands;
    public ISyntaxHighlighter Highlighter => _highlighter;
    public LanguageClient Lsp => _lsp;
    public MockLanguageClient MockLsp => _mockLsp;

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        UiDispatcher.Invoke(() => OutputLines.Add(line));
    }

    public DocumentDockable OpenDocument(DocumentModel model)
    {
        var existing = Documents.FirstOrDefault(d => d.Model.Id == model.Id);
        if (existing is not null)
        {
            Factory.SetActiveDockable(existing);
            return existing;
        }

        var dockable = new DocumentDockable(model, _highlighter, _syntaxTheme);
        dockable.CaretMoved += (_, _) =>
        {
            _documents.SetActive(model);
            RefreshStatus();
            UpdateWindowTitle();
        };
        model.DirtyChanged += (_, _) =>
        {
            dockable.RefreshTitle();
            RefreshStatus();
            UpdateWindowTitle();
        };
        Documents.Add(dockable);
        Factory.AddDocument(dockable);
        _documents.SetActive(model);
        RefreshStatus();
        UpdateWindowTitle();
        Log($"Opened {model.Title}");
        return dockable;
    }

    public void UpdateWindowTitle()
    {
        var folder = _workspace.Folder?.Name;
        var doc = _documents.ActiveDocument;
        var file = doc?.DisplayTitle;

        if (folder is not null && file is not null)
            WindowTitle = $"{file} — {folder} — Edit";
        else if (folder is not null)
            WindowTitle = $"{folder} — Edit";
        else if (file is not null)
            WindowTitle = $"{file} — Edit";
        else
            WindowTitle = "Edit";
    }

    public DocumentDockable? FindDockable(DocumentModel model) =>
        Documents.FirstOrDefault(d => d.Model.Id == model.Id);

    public void RefreshExplorer()
    {
        var nodes = _workspace.GetTree().ToList();
        UiDispatcher.Invoke(() => ExplorerNodes = new ObservableCollection<FileNode>(nodes));
    }

    private void ScheduleRefreshExplorer()
    {
        _explorerRefreshDebounce?.Cancel();
        _explorerRefreshDebounce = new CancellationTokenSource();
        var token = _explorerRefreshDebounce.Token;
        UiDispatcher.Post(() => _ = DebounceExplorerRefreshAsync(token));
    }

    private async Task DebounceExplorerRefreshAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
            RefreshExplorer();
        }
        catch (OperationCanceledException)
        {
            // A newer refresh was scheduled.
        }
    }

    public void RefreshStatus()
    {
        var doc = _documents.ActiveDocument;
        var lspStatus = _lsp.Status != "LSP: Off" ? _lsp.Status : _mockLsp.Status;
        _statusBar.UpdateFrom(doc, lspStatus);
        OnPropertyChanged(nameof(StatusBar));
    }

    public void RefreshProblemsFromLsp()
    {
        var lines = new List<string>();
        var editorDiags = new List<EditorDiagnostic>();
        foreach (var d in _lsp.Diagnostics)
        {
            lines.Add($"{d.Severity} ({d.Line + 1},{d.Character + 1}): {d.Message}");
            var doc = _documents.ActiveDocument;
            if (doc is not null)
            {
                var start = doc.Buffer.GetOffset(d.Line, d.Character);
                editorDiags.Add(new EditorDiagnostic
                {
                    StartOffset = start,
                    EndOffset = Math.Min(doc.Buffer.Length, start + 1),
                    Severity = d.Severity,
                    Message = d.Message
                });
            }
        }

        foreach (var d in _mockLsp.Diagnostics)
        {
            lines.Add($"{d.Severity} ({d.Line + 1},{d.Character + 1}): {d.Message}");
            var doc = _documents.ActiveDocument;
            if (doc is not null)
            {
                var start = doc.Buffer.GetOffset(d.Line, d.Character);
                editorDiags.Add(new EditorDiagnostic
                {
                    StartOffset = start,
                    EndOffset = Math.Min(doc.Buffer.Length, start + Math.Max(1, d.Message.Length / 4)),
                    Severity = d.Severity,
                    Message = d.Message
                });
            }
        }

        UiDispatcher.Invoke(() =>
        {
            Problems = new ObservableCollection<string>(lines);
            ApplyDiagnosticsToActiveEditor(editorDiags);
        });
    }

    public void SaveLayout()
    {
        try
        {
            var state = new LayoutState
            {
                SchemaVersion = 1,
                Documents = Documents.Select(d => d.Model.Path ?? d.Model.Title).ToArray(),
                Workspace = _workspace.Folder?.Path,
                DockProportions =
                {
                    ["left"] = 0.22,
                    ["bottom"] = 0.25
                }
            };
            File.WriteAllText(AppPaths.LayoutPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            Log("Layout saved (schema v1)");
        }
        catch (Exception ex)
        {
            Log($"Layout save failed: {ex.Message}");
        }
    }

    public void ApplyDiagnosticsToActiveEditor(IEnumerable<EditorDiagnostic> diagnostics)
    {
        var active = Documents.FirstOrDefault(d => d.Model == _documents.ActiveDocument);
        if (active is null) return;
        active.Editor.Diagnostics = diagnostics.ToList();
    }

    public async Task ShowHoverAsync()
    {
        var active = Documents.FirstOrDefault(d => d.Model == _documents.ActiveDocument);
        if (active is null) return;
        var hover = await _lsp.HoverAsync(active.Model, active.Model.CaretPosition);
        if (hover is null)
        {
            var mock = _mockLsp.Hover(active.Model, active.Model.CaretPosition);
            active.Editor.SetHover(mock.Contents);
        }
        else
        {
            active.Editor.SetHover(hover.Contents);
        }
    }

    private void RegisterBuiltinCommands()
    {
        _commands.Register(new DelegateCommand("file.new", "New File", _ =>
        {
            OpenDocument(_documents.CreateUntitled());
            return Task.CompletedTask;
        }, "Ctrl+N"));

        _commands.Register(new DelegateCommand("workbench.action.files.save", "Save", _ =>
        {
            // Actual save (including Save As for untitled) is handled by MainWindow.
            // This command is a marker so keybinding dispatch can resolve Ctrl+S.
            return Task.CompletedTask;
        }, "Ctrl+S"));

        _commands.Register(new DelegateCommand("edit.undo", "Undo", _ =>
        {
            _documents.ActiveDocument?.Buffer.Undo();
            RefreshStatus();
            UpdateWindowTitle();
            return Task.CompletedTask;
        }, "Ctrl+Z"));

        _commands.Register(new DelegateCommand("edit.redo", "Redo", _ =>
        {
            _documents.ActiveDocument?.Buffer.Redo();
            RefreshStatus();
            UpdateWindowTitle();
            return Task.CompletedTask;
        }, "Ctrl+Y"));

        _commands.Register(new DelegateCommand("editor.action.showHover", "Show Hover", async _ =>
        {
            await ShowHoverAsync();
        }, "Ctrl+K Ctrl+I"));

        _commands.Register(new DelegateCommand("layout.save", "Save Layout", _ =>
        {
            SaveLayout();
            return Task.CompletedTask;
        }));

        _commands.Register(new DelegateCommand("layout.reset", "Reset Layout", _ =>
        {
            Layout = Factory.CreateLayout();
            if (Layout is not null)
                Factory.InitLayout(Layout);
            Log("Layout reset");
            OnPropertyChanged(nameof(Layout));
            return Task.CompletedTask;
        }));
    }
}

public sealed class ShellDockFactory : Dock.Model.Mvvm.Factory
{
    private readonly ShellViewModel _shell;

    public ShellDockFactory(ShellViewModel shell) => _shell = shell;

    public override IRootDock CreateLayout()
    {
        var explorerHost = new ContentControl { Name = "ExplorerHost" };
        var problemsHost = new ContentControl { Name = "ProblemsHost" };
        var outputHost = new ContentControl { Name = "OutputHost" };

        var explorer = new ToolDockable("explorer", "Explorer", explorerHost);
        var problems = new ToolDockable("problems", "Problems", problemsHost);
        var output = new ToolDockable("output", "Output", outputHost);

        _shell.ToolContents.Clear();
        _shell.ToolContents.Add(explorer);
        _shell.ToolContents.Add(problems);
        _shell.ToolContents.Add(output);

        var documentDock = new DocumentDock
        {
            Id = "documents",
            IsCollapsable = false,
            CanCreateDocument = true
        };
        documentDock.VisibleDockables = CreateList<IDockable>();

        var leftTools = new ToolDock
        {
            Id = "left",
            Proportion = 0.22,
            Alignment = Alignment.Left,
            ActiveDockable = explorer
        };
        leftTools.VisibleDockables = CreateList<IDockable>(explorer);

        var bottomTools = new ToolDock
        {
            Id = "bottom",
            Proportion = 0.25,
            Alignment = Alignment.Bottom,
            ActiveDockable = problems
        };
        bottomTools.VisibleDockables = CreateList<IDockable>(problems, output);

        var center = new ProportionalDock
        {
            Orientation = Dock.Model.Core.Orientation.Vertical
        };
        center.VisibleDockables = CreateList<IDockable>(
            documentDock,
            new ProportionalDockSplitter(),
            bottomTools);

        var main = new ProportionalDock
        {
            Orientation = Dock.Model.Core.Orientation.Horizontal
        };
        main.VisibleDockables = CreateList<IDockable>(
            leftTools,
            new ProportionalDockSplitter(),
            center);

        var root = CreateRootDock();
        root.Id = "root";
        root.Title = "Edit";
        root.VisibleDockables = CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        return root;
    }

    public void AddDocument(DocumentDockable document)
    {
        if (_shell.Layout is null) return;
        var docs = FindDocumentDock(_shell.Layout);
        if (docs is null) return;
        AddDockable(docs, document);
        SetActiveDockable(document);
    }

    private static DocumentDock? FindDocumentDock(IDockable dockable)
    {
        if (dockable is DocumentDock dd) return dd;
        if (dockable is IDock { VisibleDockables: not null } dock)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var found = FindDocumentDock(child);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
