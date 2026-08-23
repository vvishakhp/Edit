using System.Globalization;
using System.Text.Json;

namespace Edit.Core;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor FromHex(string hex)
    {
        hex = hex.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length == 6)
        {
            return new RgbColor(
                byte.Parse(hex[..2], NumberStyles.HexNumber),
                byte.Parse(hex[2..4], NumberStyles.HexNumber),
                byte.Parse(hex[4..6], NumberStyles.HexNumber));
        }
        throw new FormatException($"Invalid hex color: #{hex}");
    }
}

public interface ISyntaxColorTheme
{
    RgbColor DefaultColor { get; }
    RgbColor GetColor(string tokenType);
}

public sealed class SyntaxColorTheme : ISyntaxColorTheme
{
    private readonly Dictionary<string, RgbColor> _tokenTypes;
    public RgbColor DefaultColor { get; }

    private SyntaxColorTheme(RgbColor defaultColor, Dictionary<string, RgbColor> tokenTypes)
    {
        DefaultColor = defaultColor;
        _tokenTypes = tokenTypes;
    }

    public RgbColor GetColor(string tokenType)
    {
        if (string.IsNullOrEmpty(tokenType)) return DefaultColor;
        return _tokenTypes.TryGetValue(tokenType, out var c) ? c : DefaultColor;
    }

    public static SyntaxColorTheme LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return CreateDefault();

        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize<ThemeDto>(json, JsonOptions)
                  ?? throw new InvalidOperationException($"Failed to parse theme: {path}");

        var defaultColor = RgbColor.FromHex(dto.Default ?? "#D4D4D4");
        var map = new Dictionary<string, RgbColor>(StringComparer.OrdinalIgnoreCase);
        if (dto.TokenTypes is not null)
        {
            foreach (var (key, value) in dto.TokenTypes)
                map[key] = RgbColor.FromHex(value);
        }

        return new SyntaxColorTheme(defaultColor, map);
    }

    public static SyntaxColorTheme CreateDefault()
    {
        var path = FindBundledThemePath();
        return path is not null ? LoadFromFile(path) : CreateBuiltInDefault();
    }

    private static SyntaxColorTheme CreateBuiltInDefault()
    {
        var map = new Dictionary<string, RgbColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["comment"] = RgbColor.FromHex("#6A9955"),
            ["string"] = RgbColor.FromHex("#CE9178"),
            ["keyword"] = RgbColor.FromHex("#569CD6"),
            ["number"] = RgbColor.FromHex("#B5CEA8"),
            ["constant"] = RgbColor.FromHex("#4FC1FF"),
            ["type"] = RgbColor.FromHex("#4EC9B0"),
            ["variable"] = RgbColor.FromHex("#9CDCFE"),
            ["function"] = RgbColor.FromHex("#DCDCAA"),
            ["property"] = RgbColor.FromHex("#9CDCFE"),
        };
        return new SyntaxColorTheme(RgbColor.FromHex("#D4D4D4"), map);
    }

    public static string? FindBundledThemePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "themes", "syntax-colors.json"),
            Path.Combine(AppContext.BaseDirectory, "syntax-colors.json")
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "themes", "syntax-colors.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ThemeDto
    {
        public string? Default { get; set; }
        public Dictionary<string, string>? TokenTypes { get; set; }
    }
}
