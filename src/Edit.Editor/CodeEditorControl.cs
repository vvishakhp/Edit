using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Edit.Core;
using Edit.Editor.Input;
using Edit.Editor.Rendering;
using Edit.Text;
using Edit.TreeSitter;

namespace Edit.Editor;

public sealed class CodeEditorControl : Control
{
    public static readonly StyledProperty<DocumentModel?> DocumentProperty =
        AvaloniaProperty.Register<CodeEditorControl, DocumentModel?>(nameof(Document));

    public static readonly StyledProperty<ISyntaxHighlighter?> HighlighterProperty =
        AvaloniaProperty.Register<CodeEditorControl, ISyntaxHighlighter?>(nameof(Highlighter));

    public static readonly StyledProperty<IReadOnlyList<EditorDiagnostic>?> DiagnosticsProperty =
        AvaloniaProperty.Register<CodeEditorControl, IReadOnlyList<EditorDiagnostic>?>(nameof(Diagnostics));

    public static readonly StyledProperty<ISyntaxColorTheme?> SyntaxThemeProperty =
        AvaloniaProperty.Register<CodeEditorControl, ISyntaxColorTheme?>(nameof(SyntaxTheme));

    private readonly EditorInteractionState _state = new();
    private readonly EditorEditOperations _edits;
    private readonly EditorPointerHandler _pointer;

    public CodeEditorControl()
    {
        _edits = new EditorEditOperations(_state);
        _pointer = new EditorPointerHandler(
            _state,
            () => Document,
            () => Highlighter,
            point => HitTestOffset(point),
            CreatePointerEventArgs,
            NotifySelectionChanged,
            () => CaretMoved?.Invoke(this, EventArgs.Empty),
            InvalidateVisual);

        _pointer.Pressed += (_, args) => EditorPointerPressed?.Invoke(this, args);
        _pointer.Moved += (_, args) => EditorPointerMoved?.Invoke(this, args);
        _pointer.Released += (_, args) => EditorPointerReleased?.Invoke(this, args);
        _pointer.Hover += (_, args) => EditorPointerHover?.Invoke(this, args);
        _pointer.DoubleClicked += (_, args) => EditorPointerDoubleClicked?.Invoke(this, args);

        Focusable = true;
        ClipToBounds = true;
        Name = "CodeEditor";
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height;
        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshSyntax();
        InvalidateVisual();
    }

    public DocumentModel? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public ISyntaxHighlighter? Highlighter
    {
        get => GetValue(HighlighterProperty);
        set => SetValue(HighlighterProperty, value);
    }

    public IReadOnlyList<EditorDiagnostic>? Diagnostics
    {
        get => GetValue(DiagnosticsProperty);
        set => SetValue(DiagnosticsProperty, value);
    }

    public ISyntaxColorTheme? SyntaxTheme
    {
        get => GetValue(SyntaxThemeProperty);
        set => SetValue(SyntaxThemeProperty, value);
    }

    public string? HoverText => _state.HoverText;

    /// <summary>Half-open UTF-16 selection range, or empty when nothing is selected.</summary>
    public TextRange Selection => _state.Selection.Range;

    public void SetHover(string? text)
    {
        _state.HoverText = text;
        InvalidateVisual();
    }

    public event EventHandler? CaretMoved;
    public event EventHandler? SelectionChanged;
    public event EventHandler<EditorPointerEventArgs>? EditorPointerPressed;
    public event EventHandler<EditorPointerEventArgs>? EditorPointerMoved;
    public event EventHandler<EditorPointerEventArgs>? EditorPointerReleased;
    public event EventHandler<EditorPointerEventArgs>? EditorPointerHover;
    public event EventHandler<EditorPointerEventArgs>? EditorPointerDoubleClicked;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            if (change.OldValue is DocumentModel oldDoc)
                oldDoc.Buffer.Changed -= OnBufferChanged;
            if (change.NewValue is DocumentModel newDoc)
            {
                newDoc.Buffer.Changed += OnBufferChanged;
                RefreshSyntax();
            }
            InvalidateVisual();
        }
        else if (change.Property == HighlighterProperty)
        {
            RefreshSyntax();
            InvalidateVisual();
        }
        else if (change.Property == DiagnosticsProperty)
        {
            InvalidateVisual();
        }
    }

    private void OnBufferChanged(object? sender, Edit.Text.TextChangedEventArgs e)
    {
        RefreshSyntax();
        Dispatcher.UIThread.Post(InvalidateVisual);
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSyntax() =>
        _state.Syntax.Refresh(Document, Highlighter);

    public override void Render(DrawingContext context)
    {
        // Capture all Avalonia/document state on the UI thread — ICustomDrawOperation.Render
        // runs on the composition/render thread and must not touch AvaloniaObject properties.
        var snapshot = EditorSnapshotBuilder.Build(Document, _state, Bounds.Height, Diagnostics);
        var theme = SyntaxTheme ?? SyntaxColorTheme.CreateDefault();
        context.Custom(new EditorDrawOperation(snapshot, new Rect(Bounds.Size), theme));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pointer.HandlePressed(e, this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _pointer.HandleMoved(e, this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _pointer.HandleReleased(e, this);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _state.Scroll.ScrollByWheel(e.Delta.Y);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var doc = Document;
        if (doc is null) return;

        EditorKeyboardHandler.TryHandle(
            e,
            doc,
            Highlighter,
            _state,
            _edits,
            Bounds.Height,
            NotifySelectionChanged,
            RefreshAfterEdit);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        var doc = Document;
        if (doc is null || string.IsNullOrEmpty(e.Text)) return;
        if (e.Text is "\n" or "\r" or "\r\n") return;

        NotifySelectionChanged(_edits.ReplaceSelection(doc, e.Text));
        _state.Syntax.UpdateBrackets(Highlighter, doc.CaretOffset);
        _state.Scroll.EnsureCaretVisible(doc.Buffer.GetPosition(doc.CaretOffset).Line, Bounds.Height);
        RefreshAfterEdit();
        e.Handled = true;
    }

    public int HitTestOffset(Point point)
    {
        var doc = Document;
        if (doc is null) return 0;
        return EditorHitTester.OffsetAt(point, _state.Scroll.ScrollY, doc.Buffer);
    }

    public TextPosition HitTestPosition(Point point)
    {
        var doc = Document;
        if (doc is null) return default;
        return EditorHitTester.PositionAt(point, _state.Scroll.ScrollY, doc.Buffer);
    }

    public TextRange GetWordAtPointer(Point point)
    {
        var doc = Document;
        if (doc is null) return default;
        return EditorHitTester.WordAt(point, _state.Scroll.ScrollY, doc.Buffer);
    }

    public HighlightSpan? GetHighlightAtOffset(int offset) =>
        _state.Syntax.GetHighlightAt(offset);

    /// <summary>Selects the word containing <paramref name="offset"/> (used by double-click and tests).</summary>
    public void SelectWordAt(int offset)
    {
        var doc = Document;
        if (doc is null) return;
        _pointer.SelectWordAt(doc, offset);
        _state.Syntax.UpdateBrackets(Highlighter, doc.CaretOffset);
        InvalidateVisual();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects the line containing <paramref name="offset"/> (used by triple-click and tests).</summary>
    public void SelectLineAt(int offset)
    {
        var doc = Document;
        if (doc is null) return;
        _pointer.SelectLineAt(doc, offset);
        _state.Syntax.UpdateBrackets(Highlighter, doc.CaretOffset);
        InvalidateVisual();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    private EditorPointerEventArgs CreatePointerEventArgs(Point point, KeyModifiers modifiers, int clickCount)
    {
        var doc = Document;
        var offset = HitTestOffset(point);
        var position = doc?.Buffer.GetPosition(offset) ?? default;
        var word = doc?.Buffer.GetWordAt(offset) ?? default;
        return new EditorPointerEventArgs
        {
            Point = point,
            Offset = offset,
            Position = position,
            Word = word,
            Token = GetHighlightAtOffset(offset),
            Modifiers = modifiers,
            ClickCount = clickCount
        };
    }

    private void NotifySelectionChanged(TextRange previous)
    {
        if (_state.Selection.HasChangedSince(previous))
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshAfterEdit()
    {
        InvalidateVisual();
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }
}
