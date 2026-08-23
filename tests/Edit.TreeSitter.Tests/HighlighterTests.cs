using Edit.Platform;
using Edit.Text;
using Edit.TreeSitter;
using FluentAssertions;
using Xunit;

namespace Edit.TreeSitter.Tests;

public class HighlighterTests
{
    private static TreeSitterHighlighter CreateHighlighter()
    {
        var root = FindNativesRoot();
        return TreeSitterHighlighter.CreateDefault(root);
    }

    private static string FindNativesRoot() => AppPaths.TreeSitterNativesRoot();

    private static bool HasBuiltGrammar(string root) =>
        Directory.Exists(Path.Combine(root, "csharp"));

    private static void RequireNatives(string root)
    {
        if (!HasBuiltGrammar(root))
            throw new InvalidOperationException(
                $"Tree-sitter natives not found at {root}. Download or build natives for this platform first.");
    }

    [Fact]
    public void Native_tree_sitter_highlights_csharp()
    {
        var root = FindNativesRoot();
        RequireNatives(root);
        var hl = CreateHighlighter();
        var text = "public class Foo { string s = \"hi\"; // c\n}";
        hl.UpdateDocument(new TextBufferSnapshot(text, 1, EndOfLineKind.Lf, 2), "csharp");
        hl.UsesNativeTreeSitter.Should().BeTrue();
        var spans = hl.GetHighlights(0, text.Length);
        spans.Should().NotBeEmpty();
        spans.Any(s => s.CaptureName is "keyword" or "comment" or "string").Should().BeTrue();
    }

    [Fact]
    public void Matching_brackets_via_tree_sitter()
    {
        var root = FindNativesRoot();
        RequireNatives(root);
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
        var root = FindNativesRoot();
        RequireNatives(root);
        var hl = CreateHighlighter();
        var text = "void M() {\n";
        hl.UpdateDocument(new TextBufferSnapshot(text, 1, EndOfLineKind.Lf, 2), "csharp");
        var indent = hl.ComputeIndentOnEnter(text.Length - 1);
        indent.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Json_uses_native_tree_sitter()
    {
        var root = FindNativesRoot();
        RequireNatives(root);
        var hl = CreateHighlighter();
        var text = "{ \"a\": 1 }";
        hl.UpdateDocument(new TextBufferSnapshot(text, 1, EndOfLineKind.Lf, 1), "json");
        hl.UsesNativeTreeSitter.Should().BeTrue();
        hl.GetHighlights(0, text.Length).Should().NotBeEmpty();
    }
}
