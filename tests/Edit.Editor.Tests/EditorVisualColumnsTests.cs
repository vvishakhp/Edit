using Edit.Editor;
using FluentAssertions;
using Xunit;

namespace Edit.Editor.Tests;

public class EditorVisualColumnsTests
{
    [Fact]
    public void Tab_expands_to_indent_size()
    {
        var line = "\tfoo";
        EditorVisualColumns.GetVisualColumn(line, 0).Should().Be(0);
        EditorVisualColumns.GetVisualColumn(line, 1).Should().Be(4);
        EditorVisualColumns.GetVisualColumn(line, 2).Should().Be(5);
        EditorVisualColumns.GetCharIndex(line, 0).Should().Be(0);
        EditorVisualColumns.GetCharIndex(line, 3).Should().Be(0); // still inside tab
        EditorVisualColumns.GetCharIndex(line, 4).Should().Be(1);
        EditorVisualColumns.GetCharIndex(line, 5).Should().Be(2);
    }
}
