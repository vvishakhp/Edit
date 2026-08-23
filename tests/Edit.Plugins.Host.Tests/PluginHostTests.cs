using Edit.ComponentModel;
using Edit.Core;
using Edit.Plugins.Host;
using Edit.Plugins.Sample;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Edit.Plugins.Host.Tests;

public class PluginHostTests
{
    [Fact]
    public async Task Loads_in_process_sample_plugin()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var commands = new CommandRegistry();
        var tools = new ToolWindowRegistry();
        var context = new DefaultPluginContext(services, commands, tools);
        var host = new PluginHost(NullLogger<PluginHost>.Instance);

        await host.LoadInProcessAsync(new[] { new SampleToolPlugin() }, context);

        host.Plugins.Should().ContainSingle(p => p.Id == "edit.sample");
        tools.All.Should().Contain(t => t.Id == "sample.tool");
        commands.Get("sample.hello").Should().NotBeNull();
    }

    [Fact]
    public void NoOp_extension_host_starts()
    {
        var host = new NoOpExtensionHost();
        host.StartAsync().IsCompletedSuccessfully.Should().BeTrue();
    }
}
