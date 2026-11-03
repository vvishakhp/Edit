using Edit.Lsp;
using Edit.Platform;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Edit.Lsp.Tests;

public class LanguageServerRegistryTests
{
    [Fact]
    public void Loads_gopls_and_rust_analyzer_from_config()
    {
        var path = AppPaths.LanguageServersPath;
        File.Exists(path).Should().BeTrue($"expected config at {path}");

        var registry = new LanguageServerRegistry(NullLogger<LanguageServerRegistry>.Instance);
        registry.Servers.Should().Contain(s => s.Id == "gopls");
        registry.Servers.Should().Contain(s => s.Id == "rust-analyzer");

        var go = registry.Servers.First(s => s.Id == "gopls");
        go.Protocol.Should().Be("lsp");
        go.Transport.Should().Be("stdio");
        go.FileGlobs.Should().Contain("**/*.go");
        go.LanguageIds.Should().Contain("go");

        var rust = registry.Servers.First(s => s.Id == "rust-analyzer");
        rust.Command.Should().Be("rust-analyzer");
        rust.FileGlobs.Should().Contain("**/*.rs");
    }
}

public class GlobMatcherTests
{
    [Theory]
    [InlineData("/home/a/main.go", "**/*.go", true)]
    [InlineData("/home/a/main.rs", "**/*.rs", true)]
    [InlineData("/home/a/main.go", "**/*.rs", false)]
    [InlineData("/home/a/main.rs", "**/*", true)]
    public void Matches_globs(string path, string glob, bool expected) =>
        GlobMatcher.IsMatch(path, glob).Should().Be(expected);
}

public class MarkupParserTests
{
    [Fact]
    public void Parses_markdown_markup_content()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{"kind":"markdown","value":"**Hello** `world`"}""");
        var (text, kind) = MarkupParser.ParseContents(doc.RootElement);
        kind.Should().Be("markdown");
        text.Should().Contain("Hello");
        MarkdownLite.ToPlainDisplay(text, kind).Should().Contain("Hello").And.Contain("world");
    }
}

public class LspContributionTests
{
    [Fact]
    public async Task Registry_dispatches_custom_method_and_command()
    {
        var registry = new Edit.ComponentModel.LspContributionRegistry();
        registry.RegisterMethod(new Edit.ComponentModel.DelegateLspMethodHandler(
            "edit/ping", true, (_, _) => Task.FromResult<object?>("pong")));
        registry.RegisterCommand(new Edit.ComponentModel.DelegateLspCommandHandler(
            "edit.cmd", (_, _) => Task.FromResult<object?>("ok")));

        var method = registry.GetMethod("edit/ping");
        method.Should().NotBeNull();
        (await method!.HandleAsync(null)).Should().Be("pong");

        var cmd = registry.GetCommand("edit.cmd");
        (await cmd!.ExecuteAsync(null)).Should().Be("ok");
    }
}
