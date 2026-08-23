using System.Reflection;
using SkiaSharp;

namespace Edit.Editor;

/// <summary>
/// Resolves the editor monospace typeface and grid metrics used for caret/hit-testing.
/// Always prefers the bundled JetBrains Mono NL so advances match what Skia draws.
/// </summary>
public static class EditorFonts
{
    public const float DefaultSize = 14f;

    private static readonly Lazy<SKTypeface> TypefaceLazy = new(LoadBundledOrThrow);
    private static readonly Lazy<EditorFontMetrics> MetricsLazy = new(Measure);

    public static SKTypeface Typeface => TypefaceLazy.Value;
    public static EditorFontMetrics Metrics => MetricsLazy.Value;
    public static string ResolvedFamilyName => Typeface.FamilyName;

    public static SKFont CreateFont(float size = DefaultSize) =>
        new(Typeface, size)
        {
            Edging = SKFontEdging.Antialias,
            // Linear metrics keep advances consistent with our fixed grid (no hinting snap).
            Hinting = SKFontHinting.None,
            Subpixel = true,
            LinearMetrics = true
        };

    private static SKTypeface LoadBundledOrThrow()
    {
        var bundled = LoadBundled();
        if (bundled is not null)
            return bundled;

        // Last-resort system faces (still verify fixed pitch).
        foreach (var family in new[]
                 {
                     "JetBrains Mono NL",
                     "JetBrainsMono NL",
                     "JetBrains Mono",
                     "JetBrainsMono Nerd Font Mono",
                     "JetBrainsMono Nerd Font"
                 })
        {
            var face = SKTypeface.FromFamilyName(family, SKFontStyle.Normal);
            if (face is not null && face.IsFixedPitch &&
                face.FamilyName.Contains("JetBrains", StringComparison.OrdinalIgnoreCase))
                return face;
            face?.Dispose();
        }

        throw new InvalidOperationException(
            "Bundled JetBrains Mono NL font resource is missing from Edit.Editor.");
    }

    private static SKTypeface? LoadBundled()
    {
        const string resourceName = "Edit.Editor.Fonts.JetBrainsMonoNL-Regular.ttf";
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is not null)
            return SKTypeface.FromStream(stream);

        var match = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("JetBrainsMonoNL-Regular.ttf", StringComparison.OrdinalIgnoreCase));
        if (match is null) return null;
        using var alt = asm.GetManifestResourceStream(match);
        return alt is null ? null : SKTypeface.FromStream(alt);
    }

    private static EditorFontMetrics Measure()
    {
        using var font = CreateFont();

        // Use the same advance Skia applies when drawing (glyph widths), not SKPaint.MeasureText,
        // which can disagree under hinting and caused caret drift.
        var glyphs = new ushort[1];
        font.GetGlyphs("M".AsSpan(), glyphs.AsSpan());
        var widths = new float[1];
        font.GetGlyphWidths(glyphs, widths, []);
        var charWidth = widths[0] > 0 ? widths[0] : 8.4f;

        // Sanity: sample ASCII should share the same advance on a true mono face.
        AssertMonospace(font, charWidth);

        var metrics = font.Metrics;
        var lineHeight = (float)Math.Ceiling(metrics.Descent - metrics.Ascent + metrics.Leading);
        if (lineHeight < charWidth * 1.2f) lineHeight = 20f;
        var baseline = -metrics.Ascent + (lineHeight - (-metrics.Ascent + metrics.Descent)) / 2f;
        if (baseline <= 0) baseline = 14f;
        return new EditorFontMetrics(charWidth, lineHeight, baseline);
    }

    private static void AssertMonospace(SKFont font, float expectedWidth)
    {
        const string sample =
            " iIl1|!.,:;`'\"()[]{}<>/\\-+*=@#$%^&_~0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var glyphs = new ushort[sample.Length];
        font.GetGlyphs(sample.AsSpan(), glyphs.AsSpan());
        var widths = new float[sample.Length];
        font.GetGlyphWidths(glyphs, widths, []);
        for (var i = 0; i < widths.Length; i++)
        {
            if (Math.Abs(widths[i] - expectedWidth) > 0.01f)
            {
                throw new InvalidOperationException(
                    $"Editor font is not monospace: '{sample[i]}' advance={widths[i]} expected={expectedWidth} family={Typeface.FamilyName}");
            }
        }
    }
}

public readonly record struct EditorFontMetrics(float CharWidth, float LineHeight, float Baseline);
