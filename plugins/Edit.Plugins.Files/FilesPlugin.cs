using Edit.ComponentModel;
using Edit.Core;

namespace Edit.Plugins.Files;

[Plugin("edit.files", "Files", "1.0.0")]
public sealed class FilesPlugin : IPlugin
{
    public string Id => "edit.files";
    public string Name => "Files";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        // UI actions (dialogs) are handled by MainWindow; these registrations
        // exist so keybinding lookup can resolve Ctrl+S / Ctrl+O / Ctrl+K Ctrl+O.
        context.Commands.Register(new DelegateCommand(
            "workbench.action.files.openFolder",
            "Open Folder",
            _ => Task.CompletedTask,
            "Ctrl+K Ctrl+O"));

        context.Commands.Register(new DelegateCommand(
            "workbench.action.files.save",
            "Save",
            _ => Task.CompletedTask,
            "Ctrl+S"));

        context.Commands.Register(new DelegateCommand(
            "workbench.action.files.openFile",
            "Open File",
            _ => Task.CompletedTask,
            "Ctrl+O"));

        return Task.CompletedTask;
    }
}
