using Edit.Text;
using FluentAssertions;
using Xunit;

namespace Edit.Text.Tests;

public class TextBufferTests
{
    [Fact]
    public void Empty_buffer_has_one_line()
    {
        var buf = new TextBuffer();
        buf.LineCount.Should().Be(1);
        buf.Length.Should().Be(0);
    }

    [Fact]
    public void GetPosition_and_GetOffset_roundtrip()
    {
        var buf = new TextBuffer("hello\nworld\n!");
        buf.GetPosition(0).Should().Be(new TextPosition(0, 0));
        buf.GetPosition(6).Should().Be(new TextPosition(1, 0));
        buf.GetOffset(1, 2).Should().Be(8);
        buf.GetLineContent(1).Should().Be("world");
    }

    [Fact]
    public void ApplyEdit_insert_and_delete()
    {
        var buf = new TextBuffer("abc");
        buf.ApplyEdit(1, 0, "X");
        buf.GetText().Should().Be("aXbc");
        buf.ApplyEdit(1, 1, "");
        buf.GetText().Should().Be("abc");
    }

    [Fact]
    public void Undo_redo_works()
    {
        var buf = new TextBuffer("hi");
        buf.ApplyEdit(2, 0, "!");
        buf.GetText().Should().Be("hi!");
        buf.Undo();
        buf.GetText().Should().Be("hi");
        buf.Redo();
        buf.GetText().Should().Be("hi!");
    }

    [Fact]
    public void GetWordAt_finds_identifier()
    {
        var buf = new TextBuffer("foo bar_baz");
        var range = buf.GetWordAt(5);
        buf.GetValueInRange(range).Should().Be("bar_baz");
    }

    [Fact]
    public void Large_document_edit_is_stable()
    {
        var lines = string.Join('\n', Enumerable.Range(0, 5000).Select(i => $"line-{i}-abcdefghijklmnopqrstuvwxyz"));
        var buf = new TextBuffer(lines);
        buf.LineCount.Should().Be(5000);
        var mid = buf.GetOffset(2500, 0);
        buf.ApplyEdit(mid, 0, "INSERTED\n");
        buf.GetLineContent(2500).Should().StartWith("INSERTED");
        buf.GetPosition(mid).Line.Should().Be(2500);
    }

    [Fact]
    public void Snapshot_captures_version()
    {
        var buf = new TextBuffer("x");
        var snap = buf.CreateSnapshot();
        buf.ApplyEdit(1, 0, "y");
        snap.Text.Should().Be("x");
        buf.Version.Should().BeGreaterThan(snap.Version);
    }
}
