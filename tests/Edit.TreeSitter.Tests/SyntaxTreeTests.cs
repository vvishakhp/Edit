using Edit.TreeSitter;
using FluentAssertions;
using Xunit;

namespace Edit.TreeSitter.Tests;

public class SyntaxTreeTests
{
    private static readonly IReadOnlyList<GrammarFixture> Fixtures = TestAssets.LoadGrammarFixtures();

    public static TheoryData<string> GrammarIds()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in Fixtures)
            data.Add(fixture.GrammarId);
        return data;
    }

    [Theory]
    [MemberData(nameof(GrammarIds))]
    public void Parses_correct_s_expression_tree(string grammarId)
    {
        var fixture = Fixtures.Single(f => f.GrammarId == grammarId);

        using var parser = TreeSitterSyntaxTreeParser.CreateDefault(TestAssets.NativesRoot());
        using var result = parser.Parse(fixture.GrammarId, fixture.Source);

        result.GrammarId.Should().Be(fixture.GrammarId);
        result.HasError.Should().BeFalse($"parse of {grammarId} should not contain errors");
        result.RootType.Should().Be(fixture.RootType);
        TreeSitterSyntaxTreeParser.FormatExpression(result.Expression)
            .Should().Be(fixture.ExpectedExpression);
        IsBalancedSExpression(result.Expression).Should().BeTrue(
            $"S-expression for {grammarId} should be balanced");
    }

    [Fact]
    public void Every_manifest_grammar_has_a_syntax_tree_fixture()
    {
        using var parser = TreeSitterSyntaxTreeParser.CreateDefault(TestAssets.NativesRoot());
        var manifestIds = parser.Registry.Grammars.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var fixtureIds = Fixtures
            .Select(f => f.GrammarId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        fixtureIds.Should().Equal(manifestIds,
            "every grammar in the natives manifest must have a Fixtures/{id} asset folder");
    }

    [Fact]
    public void Every_manifest_grammar_native_library_is_present()
    {
        var root = TestAssets.NativesRoot();
        using var parser = TreeSitterSyntaxTreeParser.CreateDefault(root);

        foreach (var (id, entry) in parser.Registry.Grammars.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var path = parser.Registry.GetLibraryPath(id);
            path.Should().NotBeNull($"native library for '{id}' ({entry.Library}) should exist under test output natives");
            File.Exists(path!).Should().BeTrue();
            path!.StartsWith(root, StringComparison.Ordinal).Should().BeTrue(
                "libraries must resolve under the test output natives root");
        }
    }

    [Fact]
    public void FormatExpression_collapses_whitespace()
    {
        TreeSitterSyntaxTreeParser.FormatExpression("(a\n  (b))\n")
            .Should().Be("(a (b))");
    }

    private static bool IsBalancedSExpression(string expression)
    {
        var depth = 0;
        foreach (var ch in expression)
        {
            if (ch == '(') depth++;
            else if (ch == ')')
            {
                depth--;
                if (depth < 0) return false;
            }
        }

        return depth == 0 && expression.Contains('(', StringComparison.Ordinal);
    }
}
