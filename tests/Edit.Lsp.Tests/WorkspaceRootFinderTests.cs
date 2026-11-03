using Edit.Lsp;
using FluentAssertions;
using Xunit;

namespace Edit.Lsp.Tests;

public class WorkspaceRootFinderTests
{
    [Fact]
    public void Finds_go_mod_parent_for_go_file()
    {
        var filePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "go-workspace",
            "cmd",
            "server",
            "main.go");
        File.Exists(filePath).Should().BeTrue($"expected fixture at {filePath}");

        var root = WorkspaceRootFinder.Find(filePath, "go");
        File.Exists(Path.Combine(root, "go.mod")).Should().BeTrue();
        Path.GetFileName(root).Should().Be("go-workspace");
    }
}
