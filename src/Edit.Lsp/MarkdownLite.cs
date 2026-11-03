using System.Text;
using System.Text.RegularExpressions;

namespace Edit.Lsp;

/// <summary>Lightweight markdown → display text for hover popups.</summary>
public static class MarkdownLite
{
    public static string ToPlainDisplay(string contents, string? kind = null)
    {
        if (string.IsNullOrEmpty(contents)) return contents;
        if (!string.Equals(kind, "markdown", StringComparison.OrdinalIgnoreCase) &&
            !contents.Contains('`') && !contents.Contains('#'))
            return contents;

        var sb = new StringBuilder();
        foreach (var rawLine in contents.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine;
            if (line.StartsWith("```"))
            {
                sb.AppendLine();
                continue;
            }

            line = Regex.Replace(line, @"^#{1,6}\s*", "");
            line = Regex.Replace(line, @"\*\*(.+?)\*\*", "$1");
            line = Regex.Replace(line, @"\*(.+?)\*", "$1");
            line = Regex.Replace(line, @"`([^`]+)`", "$1");
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]+\)", "$1");
            sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }
}
