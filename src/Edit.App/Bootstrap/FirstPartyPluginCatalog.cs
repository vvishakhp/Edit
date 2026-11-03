using Edit.ComponentModel;
using Edit.Plugins.Files;
using Edit.Plugins.Git;
using Edit.Plugins.Sample;
using Edit.Plugins.Search;
using Edit.Plugins.Terminal;

namespace Edit.App.Bootstrap;

/// <summary>Built-in in-process plugins shipped with the app host.</summary>
public static class FirstPartyPluginCatalog
{
    public static IReadOnlyList<IPlugin> Create() =>
    [
        new FilesPlugin(),
        new SampleToolPlugin(),
        new SearchPlugin(),
        new GitPlugin(),
        new TerminalPlugin()
    ];
}
