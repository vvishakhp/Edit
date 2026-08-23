using Edit.Core;
using FluentAssertions;
using Xunit;

namespace Edit.Core.Tests;

public class SyntaxColorThemeTests
{
    [Fact]
    public void Known_token_returns_configured_color()
    {
        var theme = SyntaxColorTheme.CreateDefault();
        var c = theme.GetColor("keyword");
        c.R.Should().Be(0x56);
        c.G.Should().Be(0x9C);
        c.B.Should().Be(0xD6);
    }

    [Fact]
    public void Unknown_token_returns_default()
    {
        var theme = SyntaxColorTheme.CreateDefault();
        theme.GetColor("not.a.real.token").Should().Be(theme.DefaultColor);
    }

    [Fact]
    public void LoadFromFile_reads_token_types()
    {
        var path = SyntaxColorTheme.FindBundledThemePath();
        if (path is null) return;

        var theme = SyntaxColorTheme.LoadFromFile(path);
        theme.GetColor("comment").R.Should().Be(0x6A);
    }
}
