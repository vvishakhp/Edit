using Avalonia;
using Edit.Core;
using Edit.Editor;
using Edit.Text;
using Edit.TreeSitter;
using FluentAssertions;
using Xunit;

namespace Edit.Editor.Tests;

public class CodeEditorPointerTests
{
    private const double PaddingLeft = 56;

    private static CodeEditorControl CreateEditor(string text, ISyntaxHighlighter? highlighter = null)
    {
        var doc = new DocumentModel(null, new TextBuffer(text));
        var editor = new CodeEditorControl
        {
            Document = doc,
            Highlighter = highlighter
        };
        return editor;
    }

    private static Point CellPoint(int line, int column, double scrollY = 0)
    {
        var metrics = EditorFonts.Metrics;
        // Hit the center of the cell so floor((x - pad) / cw) lands on column.
        var x = PaddingLeft + column * metrics.CharWidth + metrics.CharWidth * 0.5;
        var y = line * metrics.LineHeight - scrollY + metrics.LineHeight * 0.5;
        return new Point(x, y);
    }

    [Fact]
    public void HitTestPosition_maps_pixels_to_line_and_column()
    {
        var editor = CreateEditor("hello\nworld");
        var pos = editor.HitTestPosition(CellPoint(0, 2));
        pos.Line.Should().Be(0);
        pos.Column.Should().Be(2);

        pos = editor.HitTestPosition(CellPoint(1, 4));
        pos.Line.Should().Be(1);
        pos.Column.Should().Be(4);
    }

    [Fact]
    public void HitTestOffset_matches_buffer_roundtrip()
    {
        var editor = CreateEditor("ab\ncd");
        var offset = editor.HitTestOffset(CellPoint(1, 1));
        offset.Should().Be(editor.Document!.Buffer.GetOffset(1, 1));
        editor.Document.Buffer.GetPosition(offset).Should().Be(new TextPosition(1, 1));
    }

    [Fact]
    public void GetHighlightAtOffset_returns_span_covering_offset()
    {
        var hl = new StubHighlighter(
            new HighlightSpan(0, 6, "keyword"),
            new HighlightSpan(7, 10, "type"));
        var editor = CreateEditor("public Foo", hl);
        // Assigning Highlighter triggers RefreshSyntax which loads stub spans.
        editor.GetHighlightAtOffset(2)!.Value.CaptureName.Should().Be("keyword");
        editor.GetHighlightAtOffset(8)!.Value.CaptureName.Should().Be("type");
        editor.GetHighlightAtOffset(6).Should().BeNull();
    }

    [Fact]
    public void SelectWordAt_selects_identifier()
    {
        var editor = CreateEditor("var helloWorld = 1;");
        // "helloWorld" starts at offset 4
        editor.SelectWordAt(6);
        editor.Selection.Start.Should().Be(4);
        editor.Selection.End.Should().Be(14);
        editor.Document!.CaretOffset.Should().Be(14);
        editor.Selection.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void SelectLineAt_selects_full_line_content()
    {
        var editor = CreateEditor("one\ntwo three\nfour");
        editor.SelectLineAt(editor.Document!.Buffer.GetOffset(1, 2));
        var lineStart = editor.Document.Buffer.GetOffset(1, 0);
        var lineEnd = editor.Document.Buffer.GetOffset(1, editor.Document.Buffer.GetLineLength(1));
        editor.Selection.Start.Should().Be(lineStart);
        editor.Selection.End.Should().Be(lineEnd);
        editor.Document.Buffer.GetValueInRange(editor.Selection).Should().Be("two three");
    }

    [Fact]
    public void Selection_is_empty_after_collapsed_select()
    {
        var editor = CreateEditor("abc");
        editor.SelectWordAt(0); // 'a' is a word of length 1... actually 'abc' is one word
        editor.Selection.IsEmpty.Should().BeFalse();

        // Collapse by selecting empty range via SelectWordAt on non-word? Use SelectLineAt then manually —
        // public Selection is read-only; SelectWordAt on space:
        editor = CreateEditor("a b");
        editor.SelectWordAt(1); // space -> empty word range at space
        editor.Selection.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void SelectionChanged_fires_when_range_changes()
    {
        var editor = CreateEditor("hello world");
        var count = 0;
        editor.SelectionChanged += (_, _) => count++;
        editor.SelectWordAt(0);
        count.Should().Be(1);
        editor.SelectWordAt(0); // same word again — still sets same range
        // Setting identical range should not fire
        count.Should().Be(1);
        editor.SelectWordAt(7);
        count.Should().Be(2);
    }

    private sealed class StubHighlighter : ISyntaxHighlighter
    {
        private readonly HighlightSpan[] _spans;

        public StubHighlighter(params HighlightSpan[] spans) => _spans = spans;

        public string? GrammarId { get; private set; }
        public bool UsesNativeTreeSitter => false;

        public void UpdateDocument(TextBufferSnapshot snapshot, string? grammarId) =>
            GrammarId = grammarId;

        public IReadOnlyList<HighlightSpan> GetHighlights(int start, int end) =>
            _spans.Where(s => s.End > start && s.Start < end).ToArray();

        public BracketPair? FindMatchingBracket(int caretOffset) => null;

        public int ComputeIndentOnEnter(int caretOffset, int indentSize = 4) => 0;
    }
}
