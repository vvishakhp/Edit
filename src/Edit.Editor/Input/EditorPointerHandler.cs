using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Edit.Core;
using Edit.Text;
using Edit.TreeSitter;

namespace Edit.Editor.Input;

internal sealed class EditorPointerHandler
{
    private readonly EditorInteractionState _state;
    private readonly Func<DocumentModel?> _getDocument;
    private readonly Func<ISyntaxHighlighter?> _getHighlighter;
    private readonly Func<Point, int> _hitTestOffset;
    private readonly Func<Point, KeyModifiers, int, EditorPointerEventArgs> _createEventArgs;
    private readonly Action<TextRange> _notifySelectionChanged;
    private readonly Action _notifyCaretMoved;
    private readonly Action _invalidate;

    public EditorPointerHandler(
        EditorInteractionState state,
        Func<DocumentModel?> getDocument,
        Func<ISyntaxHighlighter?> getHighlighter,
        Func<Point, int> hitTestOffset,
        Func<Point, KeyModifiers, int, EditorPointerEventArgs> createEventArgs,
        Action<TextRange> notifySelectionChanged,
        Action notifyCaretMoved,
        Action invalidate)
    {
        _state = state;
        _getDocument = getDocument;
        _getHighlighter = getHighlighter;
        _hitTestOffset = hitTestOffset;
        _createEventArgs = createEventArgs;
        _notifySelectionChanged = notifySelectionChanged;
        _notifyCaretMoved = notifyCaretMoved;
        _invalidate = invalidate;
    }

    public event EventHandler<EditorPointerEventArgs>? Pressed;
    public event EventHandler<EditorPointerEventArgs>? Moved;
    public event EventHandler<EditorPointerEventArgs>? Released;
    public event EventHandler<EditorPointerEventArgs>? Hover;
    public event EventHandler<EditorPointerEventArgs>? DoubleClicked;

    public void HandlePressed(PointerPressedEventArgs e, Control control)
    {
        control.Focus();
        var doc = _getDocument();
        if (doc is null) return;

        var point = e.GetPosition(control);
        var clickCount = e.ClickCount;
        var offset = _hitTestOffset(point);
        var previousSelection = _state.Selection.Range;

        e.Pointer.Capture(control);
        _state.IsSelecting = true;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && clickCount <= 1)
        {
            doc.CaretOffset = offset;
            _state.Selection.UpdateFromAnchor(doc.CaretOffset);
            _notifySelectionChanged(previousSelection);
        }
        else if (clickCount >= 3)
        {
            SelectLineAt(doc, offset);
        }
        else if (clickCount == 2)
        {
            SelectWordAt(doc, offset);
        }
        else
        {
            doc.CaretOffset = offset;
            _state.Selection.CollapseTo(offset);
            _notifySelectionChanged(previousSelection);
        }

        _state.Syntax.UpdateBrackets(_getHighlighter(), doc.CaretOffset);
        _invalidate();
        _notifyCaretMoved();

        var args = _createEventArgs(point, e.KeyModifiers, clickCount);
        Pressed?.Invoke(control, args);
        if (clickCount >= 2)
            DoubleClicked?.Invoke(control, args);
        e.Handled = true;
    }

    public void HandleMoved(PointerEventArgs e, Control control)
    {
        var doc = _getDocument();
        if (doc is null) return;

        var point = e.GetPosition(control);
        var args = _createEventArgs(point, e.KeyModifiers, 0);

        if (_state.IsSelecting && e.Pointer.Captured == control)
        {
            var previousSelection = _state.Selection.Range;
            doc.CaretOffset = args.Offset;
            _state.Selection.UpdateFromAnchor(doc.CaretOffset);
            _state.Syntax.UpdateBrackets(_getHighlighter(), doc.CaretOffset);
            _invalidate();
            _notifyCaretMoved();
            _notifySelectionChanged(previousSelection);
            Moved?.Invoke(control, args);
            e.Handled = true;
            return;
        }

        if (_state.LastHoverPosition != args.Position)
        {
            _state.LastHoverPosition = args.Position;
            Hover?.Invoke(control, args);
        }
    }

    public void HandleReleased(PointerReleasedEventArgs e, Control control)
    {
        if (_getDocument() is null) return;

        var point = e.GetPosition(control);
        if (e.Pointer.Captured == control)
            e.Pointer.Capture(null);
        _state.IsSelecting = false;

        Released?.Invoke(control, _createEventArgs(point, e.KeyModifiers, 0));
        e.Handled = true;
    }

    public void SelectWordAt(DocumentModel document, int offset)
    {
        var previous = _state.Selection.Range;
        _state.Selection.SelectWord(document.Buffer, offset, out var caret);
        document.CaretOffset = caret;
        _notifySelectionChanged(previous);
    }

    public void SelectLineAt(DocumentModel document, int offset)
    {
        var previous = _state.Selection.Range;
        _state.Selection.SelectLine(document.Buffer, offset, out var caret);
        document.CaretOffset = caret;
        _notifySelectionChanged(previous);
    }
}
