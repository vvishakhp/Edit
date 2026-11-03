using System.Runtime.InteropServices;
using System.Text.Json;

namespace Edit.TreeSitter.Tests;

/// <summary>
/// Resolves test assets copied next to the test assembly (never walks outside the output folder).
/// </summary>
internal static class TestAssets
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string NativesRoot()
    {
        var root = Path.Combine(BaseDirectory, "native", GetRuntimeIdentifier());
        if (!Directory.Exists(Path.Combine(root, "csharp")))
        {
            throw new InvalidOperationException(
                $"Tree-sitter natives not found at '{root}'. " +
                "Ensure native/{rid} is present in the repo and copied to the test output.");
        }

        return root;
    }

    public static string FixturesRoot => Path.Combine(BaseDirectory, "Fixtures");

    public static IReadOnlyList<GrammarFixture> LoadGrammarFixtures()
    {
        var indexPath = Path.Combine(FixturesRoot, "index.json");
        if (!File.Exists(indexPath))
            throw new InvalidOperationException($"Missing fixture index at '{indexPath}'.");

        var entries = JsonSerializer.Deserialize<List<FixtureIndexEntry>>(
                          File.ReadAllText(indexPath), JsonOptions)
                      ?? throw new InvalidOperationException("Fixture index was empty.");

        var fixtures = new List<GrammarFixture>(entries.Count);
        foreach (var entry in entries.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Sample))
                throw new InvalidOperationException("Fixture index entry is missing id or sample.");

            var dir = Path.Combine(FixturesRoot, entry.Id);
            var samplePath = Path.Combine(FixturesRoot, entry.Sample.Replace('/', Path.DirectorySeparatorChar));
            var expectedPath = Path.Combine(dir, "expected.sexp");
            var rootTypePath = Path.Combine(dir, "root-type.txt");

            if (!File.Exists(samplePath))
                throw new InvalidOperationException($"Missing sample asset '{samplePath}'.");
            if (!File.Exists(expectedPath))
                throw new InvalidOperationException($"Missing expected tree '{expectedPath}'.");
            if (!File.Exists(rootTypePath))
                throw new InvalidOperationException($"Missing root-type asset '{rootTypePath}'.");

            fixtures.Add(new GrammarFixture(
                entry.Id,
                File.ReadAllText(samplePath),
                File.ReadAllText(rootTypePath).Trim(),
                File.ReadAllText(expectedPath).Trim()));
        }

        return fixtures;
    }

    private static string GetRuntimeIdentifier()
    {
        if (OperatingSystem.IsLinux())
            return Environment.Is64BitProcess ? "linux-x64" : "linux-x86";
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

    private sealed class FixtureIndexEntry
    {
        public string? Id { get; set; }
        public string? Sample { get; set; }
    }
}

internal sealed record GrammarFixture(
    string GrammarId,
    string Source,
    string RootType,
    string ExpectedExpression);
