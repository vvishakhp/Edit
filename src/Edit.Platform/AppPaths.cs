using System.Runtime.InteropServices;

namespace Edit.Platform;

public static class AppPaths
{
    public static string UserDataDirectory
    {
        get
        {
            var overrideRoot = Environment.GetEnvironmentVariable("EDIT_USER_DATA");
            var root = string.IsNullOrWhiteSpace(overrideRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edit")
                : overrideRoot;
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string SettingsPath => Path.Combine(UserDataDirectory, "settings.json");
    public static string LayoutPath => Path.Combine(UserDataDirectory, "layout.json");

    public static string SyntaxColorsPath =>
        Path.Combine(AppContext.BaseDirectory, "themes", "syntax-colors.json");

    public static string LanguageServersPath => FindConfigFile("language-servers.json");

    public static string DebugAdaptersPath => FindConfigFile("debug-adapters.json");

    /// <summary>
    /// Resolve a shipped config JSON: output <c>config/</c>, then walk parents for repo <c>config/</c>.
    /// </summary>
    public static string FindConfigFile(string fileName)
    {
        var fromBase = Path.Combine(AppContext.BaseDirectory, "config", fileName);
        if (File.Exists(fromBase)) return fromBase;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "config", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return fromBase;
    }

    /// <summary>
    /// Root directory for self-built Tree-sitter grammar natives (e.g. tree-sitter/linux-x64).
    /// Prefers a directory that actually contains grammar shared libraries, not just queries.
    /// </summary>
    public static string TreeSitterNativesRoot()
    {
        var rid = GetRuntimeIdentifier();
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "tree-sitter", rid),
            Path.Combine(baseDir, "native", rid),
            FindInTree("native", rid),
            FindInTree("tree-sitter", rid)
        };

        string? fallback = null;
        foreach (var c in candidates)
        {
            if (c is null || !Directory.Exists(c))
                continue;
            fallback ??= c;
            if (HasGrammarLibraries(c))
                return c;
        }

        return fallback ?? Path.Combine(baseDir, "tree-sitter", rid);
    }

    private static bool HasGrammarLibraries(string nativesRoot)
    {
        // A usable natives root has at least one grammar shared library (not queries-only stubs).
        foreach (var dir in Directory.EnumerateDirectories(nativesRoot))
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("libtree-sitter-", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("tree-sitter-", StringComparison.OrdinalIgnoreCase))
                {
                    var ext = Path.GetExtension(file);
                    if (ext is ".so" or ".dylib" or ".dll")
                        return true;
                }
            }
        }

        return false;
    }

    public static string FindPluginsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "plugins");
            if (Directory.Exists(candidate)) return candidate;
            var output = Path.Combine(dir.FullName, "plugin-bin");
            if (Directory.Exists(output)) return output;
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    private static string GetRuntimeIdentifier()
    {
        if (OperatingSystem.IsLinux())
        {
            if (Environment.Is64BitProcess) return "linux-x64";
            return "linux-x86";
        }
        if (OperatingSystem.IsWindows())
            return Environment.Is64BitProcess ? "win-x64" : "win-x86";
        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "osx-arm64",
                Architecture.X64 => "osx-x64",
                _ => "osx-arm64"
            };
        }
        return "linux-x64";
    }

    private static string? FindInTree(string folder, string rid)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, folder, rid);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
