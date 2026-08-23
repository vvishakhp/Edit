namespace Edit.Core;

public sealed class EditorDiagnostic
{
    public required int StartOffset { get; init; }
    public required int EndOffset { get; init; }
    public required string Severity { get; init; } // Error, Warning, Info, Hint
    public required string Message { get; init; }
}

public sealed class LayoutState
{
    public int SchemaVersion { get; set; } = 1;
    public string[] Documents { get; set; } = Array.Empty<string>();
    public string? Workspace { get; set; }
    public Dictionary<string, double> DockProportions { get; set; } = new();
}

public static class KeybindingDefaults
{
    public static Dictionary<string, string> Create() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl+N"] = "file.new",
        ["Ctrl+S"] = "workbench.action.files.save",
        ["Ctrl+Z"] = "edit.undo",
        ["Ctrl+Y"] = "edit.redo",
        ["Ctrl+O"] = "workbench.action.files.openFile",
        ["Ctrl+K Ctrl+O"] = "workbench.action.files.openFolder",
        ["F12"] = "editor.action.revealDefinition",
        ["Ctrl+Shift+F"] = "workbench.action.findInFiles"
    };
}
