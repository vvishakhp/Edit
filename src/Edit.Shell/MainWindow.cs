using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Dock.Avalonia.Controls;
using Edit.Core;
using Edit.Workspace;

namespace Edit.Shell;

public sealed class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private readonly Dictionary<string, string> _keybindings;
    private TreeView? _explorerTree;
    private ListBox? _problemsList;
    private ListBox? _outputList;

    public MainWindow(ShellViewModel vm)
    {
        _vm = vm;
        _keybindings = KeybindingDefaults.Create();
        Name = "MainWindow";
        Width = 1280;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DataContext = vm;
        this.Bind(TitleProperty, new Binding(nameof(ShellViewModel.WindowTitle)));

        // Tunnel so shortcuts work even when the editor has focus.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        var menu = BuildMenu();
        var dock = new DockControl
        {
            Name = "RootDock",
            [!DockControl.LayoutProperty] = new Binding(nameof(ShellViewModel.Layout)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var status = BuildStatusBar();

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                menu,
                status,
                dock
            }
        };

        Opened += (_, _) =>
        {
            WireToolContents();
            if (_vm.Documents.Count == 0)
                _vm.OpenDocument(_vm.DocumentsService.CreateUntitled("// Welcome to Edit\n"));
            _vm.UpdateWindowTitle();
        };
    }

    public ShellViewModel ViewModel => _vm;

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var commandId = KeybindingDispatcher.Match(e, _vm.Commands, _keybindings);
        if (commandId is null) return;

        e.Handled = true;

        switch (commandId)
        {
            case "workbench.action.files.save":
                await SaveAsync();
                return;
            case "workbench.action.files.openFile":
                await OpenFileAsync();
                return;
            case "workbench.action.files.openFolder":
                await OpenFolderAsync();
                return;
        }

        if (_vm.Commands.Get(commandId) is not null)
            await _vm.Commands.ExecuteAsync(commandId);
    }

    private Menu BuildMenu()
    {
        var menu = new Menu
        {
            Name = "MainMenu",
            [DockPanel.DockProperty] = Avalonia.Controls.Dock.Top
        };

        menu.Items.Add(new MenuItem
        {
            Header = "_File",
            Items =
            {
                CreateMenuItem("New File", async () => await _vm.Commands.ExecuteAsync("file.new")),
                CreateMenuItem("Open File…", OpenFileAsync),
                CreateMenuItem("Open Folder…", OpenFolderAsync),
                CreateMenuItem("Save", SaveAsync),
                CreateMenuItem("Save As…", SaveAsAsync),
                new Separator(),
                CreateMenuItem("Exit", () => { Close(); return Task.CompletedTask; })
            }
        });

        menu.Items.Add(new MenuItem
        {
            Header = "_Edit",
            Items =
            {
                CreateMenuItem("Undo", async () => await _vm.Commands.ExecuteAsync("edit.undo"))
            }
        });

        menu.Items.Add(new MenuItem
        {
            Header = "_View",
            Items =
            {
                CreateMenuItem("Save Layout", async () => await _vm.Commands.ExecuteAsync("layout.save")),
                CreateMenuItem("Reset Layout", async () => await _vm.Commands.ExecuteAsync("layout.reset"))
            }
        });

        return menu;
    }

    private Control BuildStatusBar()
    {
        return new Border
        {
            Name = "StatusBar",
            Background = new SolidColorBrush(Color.Parse("#007ACC")),
            Height = 24,
            Padding = new Thickness(8, 0),
            [DockPanel.DockProperty] = Avalonia.Controls.Dock.Bottom,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    NamedText("StatusLineColumn", nameof(StatusBarModel.LineColumn)),
                    NamedText("StatusLanguage", nameof(StatusBarModel.Language)),
                    NamedText("StatusEncoding", nameof(StatusBarModel.Encoding)),
                    NamedText("StatusEol", nameof(StatusBarModel.Eol)),
                    NamedText("StatusLsp", nameof(StatusBarModel.LspStatus)),
                    NamedText("StatusDirty", nameof(StatusBarModel.Dirty))
                }
            }
        };
    }

    private TextBlock NamedText(string name, string property)
    {
        var tb = new TextBlock
        {
            Name = name,
            Foreground = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.Bind(TextBlock.TextProperty, new Binding($"StatusBar.{property}"));
        return tb;
    }

    private static MenuItem CreateMenuItem(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private void WireToolContents()
    {
        foreach (var tool in _vm.ToolContents)
        {
            if (tool.Id == "explorer" && tool.ToolContent is ContentControl explorerHost)
            {
                _explorerTree = new TreeView { Name = "ExplorerTree" };
                _explorerTree.Bind(ItemsControl.ItemsSourceProperty,
                    new Binding(nameof(ShellViewModel.ExplorerNodes)) { Source = _vm });
                _explorerTree.ItemTemplate = new FuncTreeDataTemplate<FileNode>((node, _) =>
                    new TextBlock { Text = node.Name },
                    node => node.Children);
                _explorerTree.DoubleTapped += async (_, _) =>
                {
                    if (_explorerTree.SelectedItem is FileNode { IsDirectory: false } file)
                    {
                        var doc = _vm.DocumentsService.OpenFile(file.FullPath);
                        _vm.OpenDocument(doc);
                        await _vm.Lsp.DidOpenAsync(doc);
                        _vm.UpdateWindowTitle();
                    }
                };
                explorerHost.Content = _explorerTree;
            }
            else if (tool.Id == "problems" && tool.ToolContent is ContentControl problemsHost)
            {
                _problemsList = new ListBox { Name = "ProblemsList" };
                _problemsList.Bind(ItemsControl.ItemsSourceProperty,
                    new Binding(nameof(ShellViewModel.Problems)) { Source = _vm });
                problemsHost.Content = _problemsList;
            }
            else if (tool.Id == "output" && tool.ToolContent is ContentControl outputHost)
            {
                _outputList = new ListBox { Name = "OutputList" };
                _outputList.Bind(ItemsControl.ItemsSourceProperty,
                    new Binding(nameof(ShellViewModel.OutputLines)) { Source = _vm });
                outputHost.Content = _outputList;
            }
        }
    }

    private async Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = false
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            var doc = _vm.DocumentsService.OpenFile(path);
            _vm.OpenDocument(doc);
            await _vm.Lsp.DidOpenAsync(doc);
            _vm.UpdateWindowTitle();
        }
    }

    private async Task OpenFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            _vm.Workspace.OpenFolder(path);
            _vm.RefreshExplorer();
            _vm.UpdateWindowTitle();
            _vm.Log($"Opened folder {path}");
        }
    }

    private Task SaveAsync()
    {
        var doc = _vm.DocumentsService.ActiveDocument;
        if (doc is null) return Task.CompletedTask;
        if (doc.Path is null) return SaveAsAsync();
        _vm.DocumentsService.Save(doc);
        _vm.FindDockable(doc)?.RefreshTitle();
        _vm.RefreshStatus();
        _vm.UpdateWindowTitle();
        _vm.Log($"Saved {doc.Path}");
        return Task.CompletedTask;
    }

    private async Task SaveAsAsync()
    {
        var doc = _vm.DocumentsService.ActiveDocument;
        if (doc is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As"
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        _vm.DocumentsService.Save(doc, path);
        _vm.FindDockable(doc)?.RefreshTitle();
        _vm.RefreshStatus();
        _vm.UpdateWindowTitle();
        _vm.Log($"Saved {path}");
    }
}
