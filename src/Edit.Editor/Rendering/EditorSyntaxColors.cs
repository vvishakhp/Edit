using Edit.Core;
using SkiaSharp;

namespace Edit.Editor.Rendering;

internal static class EditorSyntaxColors
{
    public static SKColor ForCapture(ISyntaxColorTheme theme, string capture)
    {
        var rgb = theme.GetColor(capture);
        return new SKColor(rgb.R, rgb.G, rgb.B);
    }

    public static SKColor Default(ISyntaxColorTheme theme)
    {
        var rgb = theme.DefaultColor;
        return new SKColor(rgb.R, rgb.G, rgb.B);
    }
}
