using Edit.Text;

namespace Edit.Editor;

/// <summary>Mutable editor state shared across input and rendering modules.</summary>
internal sealed class EditorInteractionState
{
    public EditorSelection Selection { get; } = new();
    public EditorScrollController Scroll { get; } = new();
    public EditorSyntaxState Syntax { get; } = new();
    public bool IsSelecting { get; set; }
    public TextPosition? LastHoverPosition { get; set; }
    public string? HoverText { get; set; }
}
