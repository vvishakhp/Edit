namespace Edit.Lsp;

/// <summary>Discover a workspace root for a document path (go.mod, Cargo.toml, .git, or parent dir).</summary>
public static class WorkspaceRootFinder
{
    public static string Find(string filePath, string? languageId = null)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (string.IsNullOrEmpty(dir))
            return Environment.CurrentDirectory;

        for (var current = new DirectoryInfo(dir); current is not null; current = current.Parent)
        {
            if (HasProjectMarker(current, languageId))
                return current.FullName;
        }

        return dir;
    }

    private static bool HasProjectMarker(DirectoryInfo dir, string? languageId)
    {
        var path = dir.FullName;
        return languageId?.ToLowerInvariant() switch
        {
            "go" => File.Exists(Path.Combine(path, "go.mod"))
                    || File.Exists(Path.Combine(path, "go.work"))
                    || Directory.Exists(Path.Combine(path, ".git")),
            "rust" => File.Exists(Path.Combine(path, "Cargo.toml"))
                      || Directory.Exists(Path.Combine(path, ".git")),
            "csharp" => dir.EnumerateFiles("*.sln").Any()
                        || dir.EnumerateFiles("*.csproj").Any()
                        || Directory.Exists(Path.Combine(path, ".git")),
            _ => File.Exists(Path.Combine(path, "go.mod"))
                 || File.Exists(Path.Combine(path, "go.work"))
                 || File.Exists(Path.Combine(path, "Cargo.toml"))
                 || File.Exists(Path.Combine(path, "package.json"))
                 || Directory.Exists(Path.Combine(path, ".git"))
        };
    }
}
