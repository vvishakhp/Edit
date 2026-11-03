using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Edit.ComponentModel;
using Edit.Core;
using Edit.Editor;
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

    public void AttachDocumentLsp(DocumentDockable dockable)
    {
        var model = dockable.Model;
        CancellationTokenSource? debounce = null;
        model.Buffer.Changed += (_, _) =>
        {
            debounce?.Cancel();
            debounce = new CancellationTokenSource();
            var token = debounce.Token;
            _ = DebouncedDidChangeAsync(model, token);
        };

        dockable.Editor.EditorPointerHover += async (_, args) =>
        {
            try
            {
                var pos = model.Buffer.GetPosition(args.Offset);
                var hover = await _lsp.HoverAsync(model, pos);
                if (hover is not null)
                    dockable.Editor.SetHover(MarkdownLite.ToPlainDisplay(hover.Contents, hover.Kind));
            }
            catch
            {
                // ignore hover failures
            }
        };
    }

    private async Task DebouncedDidChangeAsync(DocumentModel model, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
            await _lsp.DidChangeAsync(model);
        }
        catch (OperationCanceledException)
        {
            // newer edit
        }
        catch (Exception ex)
        {
            Log($"LSP didChange failed: {ex.Message}");
        }
    }

    public async Task EnsureLanguageServersForDocumentAsync(DocumentModel document)
    {
        if (document.Path is null) return;
        var root = WorkspaceRootFinder.Find(document.Path, document.GrammarId);
        Log($"Ensuring language servers for {document.Title} (root: {root})…");
        await _lsp.EnsureStartedForDocumentAsync(document);
        RefreshStatus();
        Log(_lsp.Status);
    }

    public async Task StartLanguageServersAsync(string workspaceRoot)
    {
        Log($"Starting language servers for {workspaceRoot}…");
        await _lsp.StartWorkspaceAsync(workspaceRoot);
        RefreshStatus();
        Log(_lsp.Status);
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
        var active = _documents.ActiveDocument;

        foreach (var d in _lsp.Diagnostics)
        {
            lines.Add($"{d.Severity} ({d.Line + 1},{d.Character + 1}): {d.Message}");
            if (active is null) continue;
            if (d.Uri is not null && active.Path is not null)
            {
                var activeUri = new Uri(active.Path).AbsoluteUri;
                if (!string.Equals(d.Uri, activeUri, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var start = active.Buffer.GetOffset(d.Line, d.Character);
            var end = active.Buffer.GetOffset(
                d.EndLine != 0 || d.EndCharacter != 0 ? d.EndLine : d.Line,
                d.EndLine != 0 || d.EndCharacter != 0 ? d.EndCharacter : d.Character + 1);
            editorDiags.Add(new EditorDiagnostic
            {
                StartOffset = start,
                EndOffset = Math.Max(start + 1, end),
                Severity = d.Severity,
                Message = d.Message
            });
        }

        foreach (var d in _mockLsp.Diagnostics)
        {
            lines.Add($"{d.Severity} ({d.Line + 1},{d.Character + 1}): {d.Message}");
            if (active is not null)
            {
                var start = active.Buffer.GetOffset(d.Line, d.Character);
                editorDiags.Add(new EditorDiagnostic
                {
                    StartOffset = start,
                    EndOffset = Math.Min(active.Buffer.Length, start + Math.Max(1, d.Message.Length / 4)),
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
            active.Editor.SetHover(MarkdownLite.ToPlainDisplay(mock.Contents, mock.Kind));
        }
        else
        {
            active.Editor.SetHover(MarkdownLite.ToPlainDisplay(hover.Contents, hover.Kind));
        }
    }

    public async Task TriggerCompletionAsync()
    {
        var active = Documents.FirstOrDefault(d => d.Model == _documents.ActiveDocument);
        if (active is null) return;
        var items = await _lsp.CompleteAsync(active.Model, active.Model.CaretPosition);
        if (items.Count == 0)
            items = _mockLsp.Complete(active.Model, active.Model.CaretPosition);
        active.Editor.ShowCompletions(items.Select(i => new CompletionListItem
        {
            Label = i.Label,
            Detail = i.Detail,
            Documentation = i.Documentation,
            InsertText = i.InsertText ?? i.Label
        }).ToList());
    }

    public async Task GoToDefinitionAsync()
    {
        var active = Documents.FirstOrDefault(d => d.Model == _documents.ActiveDocument);
        if (active is null) return;
        var locs = await _lsp.DefinitionAsync(active.Model, active.Model.CaretPosition);
        if (locs.Count == 0)
        {
            Log("No definition found");
            return;
        }

        await NavigateToLocationAsync(locs[0]);
    }

    public async Task FindReferencesAsync()
    {
        var active = Documents.FirstOrDefault(d => d.Model == _documents.ActiveDocument);
        if (active is null) return;
        var locs = await _lsp.ReferencesAsync(active.Model, active.Model.CaretPosition);
        Log(locs.Count == 0 ? "No references" : $"Found {locs.Count} reference(s)");
        foreach (var loc in locs.Take(20))
            Log($"  {loc.Uri}:{loc.Range.Start.Line + 1}:{loc.Range.Start.Character + 1}");
    }

    public async Task FormatDocumentAsync()
    {
        var active = Documents.FirstOrDefault(d => d.Model == _documents.ActiveDocument);
        if (active is null) return;
        var edits = await _lsp.FormatDocumentAsync(active.Model);
        if (edits.Count == 0)
        {
            Log("No formatting edits");
            return;
        }
        WorkspaceEditApplier.ApplyToDocument(active.Model, edits);
        active.Editor.InvalidateVisual();
        Log($"Applied {edits.Count} formatting edit(s)");
    }

    public async Task RenameSymbolAsync(string newName)
    {
        var active = Documents.FirstOrDefault(d => d.Model == _documents.ActiveDocument);
        if (active is null || string.IsNullOrWhiteSpace(newName)) return;
        var edit = await _lsp.RenameAsync(active.Model, active.Model.CaretPosition, newName);
        if (edit is null)
        {
            Log("Rename not available");
            return;
        }
        WorkspaceEditApplier.Apply(_documents, edit);
        Log($"Renamed to {newName}");
    }

    public async Task ShowSignatureHelpAsync()
    {
        var active = Documents.FirstOrDefault(d => d.Model == _documents.ActiveDocument);
        if (active is null) return;
        var help = await _lsp.SignatureHelpAsync(active.Model, active.Model.CaretPosition);
        if (help is null || help.Signatures.Length == 0)
        {
            active.Editor.SetHover(null);
            return;
        }

        var idx = help.ActiveSignature ?? 0;
        idx = Math.Clamp(idx, 0, help.Signatures.Length - 1);
        active.Editor.SetHover(help.Signatures[idx].Label);
    }

    private async Task NavigateToLocationAsync(Edit.Lsp.Protocol.Location loc)
    {
        var path = Uri.UnescapeDataString(new Uri(loc.Uri).LocalPath);
        if (!File.Exists(path))
        {
            Log($"Definition target missing: {path}");
            return;
        }

        var doc = _documents.OpenFile(path);
        var dockable = OpenDocument(doc);
        AttachDocumentLsp(dockable);
        await EnsureLanguageServersForDocumentAsync(doc);
        await _lsp.DidOpenAsync(doc);
        var offset = doc.Buffer.GetOffset(loc.Range.Start.Line, loc.Range.Start.Character);
        doc.CaretOffset = offset;
        dockable.Editor.InvalidateVisual();
        RefreshStatus();
    }

    private void RegisterBuiltinCommands()
    {
        _commands.Register(new DelegateCommand("file.new", "New File", _ =>
        {
            var doc = _documents.CreateUntitled();
            var dockable = OpenDocument(doc);
            AttachDocumentLsp(dockable);
            return Task.CompletedTask;
        }, "Ctrl+N"));

        _commands.Register(new DelegateCommand("workbench.action.files.save", "Save", async _ =>
        {
            var doc = _documents.ActiveDocument;
            if (doc is null) return;
            if (doc.Path is not null)
                await _lsp.DidSaveAsync(doc);
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

        _commands.Register(new DelegateCommand("editor.action.triggerSuggest", "Trigger Suggest", async _ =>
        {
            await TriggerCompletionAsync();
        }, "Ctrl+Space"));

        _commands.Register(new DelegateCommand("editor.action.revealDefinition", "Go to Definition", async _ =>
        {
            await GoToDefinitionAsync();
        }, "F12"));

        _commands.Register(new DelegateCommand("editor.action.referenceSearch.trigger", "Find References", async _ =>
        {
            await FindReferencesAsync();
        }, "Shift+F12"));

        _commands.Register(new DelegateCommand("editor.action.formatDocument", "Format Document", async _ =>
        {
            await FormatDocumentAsync();
        }, "Shift+Alt+F"));

        _commands.Register(new DelegateCommand("editor.action.rename", "Rename Symbol", async _ =>
        {
            await RenameSymbolAsync("NewName");
        }, "F2"));

        _commands.Register(new DelegateCommand("editor.action.triggerParameterHints", "Signature Help", async _ =>
        {
            await ShowSignatureHelpAsync();
        }, "Ctrl+Shift+Space"));

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
