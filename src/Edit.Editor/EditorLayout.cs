namespace Edit.Editor;

/// <summary>Monospace grid layout constants shared by hit-testing and rendering.</summary>
internal static class EditorLayout
{
    public const double PaddingLeft = 56;
    public const int IndentSize = 4;

    public static float LineHeight => EditorFonts.Metrics.LineHeight;
    public static float CharWidth => EditorFonts.Metrics.CharWidth;
    public static float Baseline => EditorFonts.Metrics.Baseline;
}
