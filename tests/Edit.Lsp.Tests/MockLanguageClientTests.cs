using Edit.Core;
using Edit.Lsp;
using Edit.Text;
using FluentAssertions;
using Xunit;

namespace Edit.Lsp.Tests;

public class MockLanguageClientTests
{
    [Fact]
    public void Hover_returns_word()
    {
        var docs = new DocumentService();
        var doc = docs.CreateUntitled("hello world");
        doc.CaretOffset = 1;
        var mock = new MockLanguageClient();
        var hover = mock.Hover(doc, doc.CaretPosition);
        hover.Contents.Should().Contain("hello");
    }

    [Fact]
    public void Completion_returns_items()
    {
        var docs = new DocumentService();
        var doc = docs.CreateUntitled("hel");
        var mock = new MockLanguageClient();
        var items = mock.Complete(doc, new TextPosition(0, 3));
        items.Should().NotBeEmpty();
    }

    [Fact]
    public void Diagnostics_can_be_published()
    {
        var mock = new MockLanguageClient();
        mock.PublishSampleDiagnostic(2, "sample warning");
        mock.Diagnostics.Should().ContainSingle(d => d.Message == "sample warning" && d.Line == 2);
    }
}
