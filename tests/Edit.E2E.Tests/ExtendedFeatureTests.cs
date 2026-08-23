using Edit.Plugins.Git;
using Edit.Plugins.Search;
using Edit.Plugins.Terminal;
using Edit.Dap;
using FluentAssertions;
using Xunit;

namespace Edit.E2E.Tests;

public class ExtendedFeatureTests
{
    [Fact]
    public void Search_finds_fixture_content()
    {
        var root = FindFixture();
        var hits = new SearchService().FindInFiles(root, "Hello");
        hits.Should().NotBeEmpty();
        hits.Any(h => h.Path.EndsWith("hello.cs", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public void Git_status_returns_summary()
    {
        var summary = new GitService().StatusSummary(FindRepoRoot());
        summary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Terminal_session_runs_echo()
    {
        var term = new TerminalSession();
        string output;
        if (OperatingSystem.IsWindows())
            output = await term.RunAsync("cmd.exe", "/C echo edit-terminal-ok");
        else
            output = await term.RunAsync("bash", "-lc \"echo edit-terminal-ok\"");
        output.Should().Contain("edit-terminal-ok");
    }

    [Fact]
    public void Dap_noop_client_is_available()
    {
        IDebugAdapterClient dap = new NoOpDebugAdapterClient();
        dap.Status.Should().Be("DAP: Off");
        dap.StartAsync(new DebugAdapterLaunchRequest { AdapterCommand = "none" }).IsCompletedSuccessfully.Should().BeTrue();
    }

    private static string FindFixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "sample-workspace");
            if (Directory.Exists(candidate)) return candidate;
            var copied = Path.Combine(dir.FullName, "fixtures", "sample-workspace");
            if (Directory.Exists(copied)) return copied;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("fixture missing");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, "Edit.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
