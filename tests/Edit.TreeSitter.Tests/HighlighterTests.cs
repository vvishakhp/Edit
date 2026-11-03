using Edit.Text;
using Edit.TreeSitter;
using FluentAssertions;
using Xunit;

namespace Edit.TreeSitter.Tests;

public class HighlighterTests
{
    private static TreeSitterHighlighter CreateHighlighter() =>
        TreeSitterHighlighter.CreateDefault(TestAssets.NativesRoot());

    [Fact]
    public void Native_tree_sitter_highlights_csharp()
    {
        var fixture = TestAssets.LoadGrammarFixtures().Single(f => f.GrammarId == "csharp");
        var hl = CreateHighlighter();
        hl.UpdateDocument(new TextBufferSnapshot(fixture.Source, 1, EndOfLineKind.Lf, 2), "csharp");
        hl.UsesNativeTreeSitter.Should().BeTrue();
        var spans = hl.GetHighlights(0, fixture.Source.Length);
        spans.Should().NotBeEmpty();
        spans.Any(s => s.CaptureName is "keyword" or "comment" or "string" or "type").Should().BeTrue();
    }

    [Fact]
    public void Matching_brackets_via_tree_sitter()
    {
        var hl = CreateHighlighter();
        var text = "foo(bar[0])";
        hl.UpdateDocument(new TextBufferSnapshot(text, 1, EndOfLineKind.Lf, 1), "csharp");
        var pair = hl.FindMatchingBracket(3); // '('
        pair.Should().NotBeNull();
        pair!.Value.OpenStart.Should().Be(3);
        pair.Value.CloseStart.Should().Be(10);
    }

    [Fact]
    public void Auto_indent_increases_after_brace()
    {
        var hl = CreateHighlighter();
        var text = "void M() {\n";
        hl.UpdateDocument(new TextBufferSnapshot(text, 1, EndOfLineKind.Lf, 2), "csharp");
        var indent = hl.ComputeIndentOnEnter(text.Length - 1);
        indent.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Json_uses_native_tree_sitter()
    {
        var fixture = TestAssets.LoadGrammarFixtures().Single(f => f.GrammarId == "json");
        var hl = CreateHighlighter();
        hl.UpdateDocument(new TextBufferSnapshot(fixture.Source, 1, EndOfLineKind.Lf, 1), "json");
        hl.UsesNativeTreeSitter.Should().BeTrue();
        hl.GetHighlights(0, fixture.Source.Length).Should().NotBeEmpty();
    }
}
