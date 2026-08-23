using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace Edit.Editor.Tests;

public class EditorFontsTests
{
    [Fact]
    public void Bundled_jetbrains_mono_is_used()
    {
        EditorFonts.ResolvedFamilyName.Should().ContainEquivalentOf("JetBrains");
        EditorFonts.Typeface.IsFixedPitch.Should().BeTrue();
    }

    [Fact]
    public void All_ascii_glyphs_share_char_width()
    {
        using var font = EditorFonts.CreateFont();
        var expected = EditorFonts.Metrics.CharWidth;
        expected.Should().BeGreaterThan(0);

        const string sample =
            " iIl1|!.,:;`'\"()[]{}<>/\\-+*=@#$%^&_~0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var glyphs = new ushort[sample.Length];
        font.GetGlyphs(sample.AsSpan(), glyphs.AsSpan());
        var widths = new float[sample.Length];
        font.GetGlyphWidths(glyphs, widths, []);

        for (var i = 0; i < widths.Length; i++)
            widths[i].Should().BeApproximately(expected, 0.01f, because: $"glyph '{sample[i]}'");
    }

    [Fact]
    public void Caret_grid_matches_drawn_glyph_advances_over_long_line()
    {
        using var font = EditorFonts.CreateFont();
        var cw = EditorFonts.Metrics.CharWidth;
        var line = "public class Foo { int x = 1; string s = \"hello\"; // comment 0123456789";
        var glyphs = new ushort[line.Length];
        font.GetGlyphs(line.AsSpan(), glyphs.AsSpan());
        var widths = new float[line.Length];
        font.GetGlyphWidths(glyphs, widths, []);

        float natural = 0;
        for (var i = 0; i < line.Length; i++)
        {
            (natural - i * cw).Should().BeApproximately(0, 0.01f, because: $"column {i}");
            natural += widths[i];
        }
    }
}
