using Avalonia;
using Avalonia.Input;
using Edit.Text;
using Edit.TreeSitter;

namespace Edit.Editor;

/// <summary>
/// Pointer event payload with document position and optional syntax token under the pointer.
/// </summary>
public sealed class EditorPointerEventArgs : EventArgs
{
    public Point Point { get; init; }
    public int Offset { get; init; }
    public TextPosition Position { get; init; }
    public TextRange Word { get; init; }
    public HighlightSpan? Token { get; init; }
    public KeyModifiers Modifiers { get; init; }
    public int ClickCount { get; init; }
}
