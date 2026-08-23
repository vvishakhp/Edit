using Edit.ComponentModel;

namespace Edit.Plugins.Search;

[Plugin("edit.search", "Search", "1.0.0")]
[ExportToolWindow("search.results", "Search", DefaultLocation = "Bottom")]
public sealed class SearchPlugin : IPlugin
{
    public string Id => "edit.search";
    public string Name => "Search";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        context.ToolWindows.Register(new SearchToolDescriptor());
        context.Commands.Register(new DelegateCommand(
            "workbench.action.findInFiles",
            "Find in Files",
            _ => Task.CompletedTask,
            "Ctrl+Shift+F"));
        return Task.CompletedTask;
    }
}

public sealed class SearchService
{
    public IReadOnlyList<SearchHit> FindInFiles(string root, string query, int maxResults = 200)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(query) || !Directory.Exists(root))
            return Array.Empty<SearchHit>();

        var hits = new List<SearchHit>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p is "bin" or "obj" or ".git" or "node_modules"))
                continue;

            try
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new SearchHit(file, i + 1, lines[i].Trim()));
                        if (hits.Count >= maxResults) return hits;
                    }
                }
            }
            catch
            {
                // skip unreadable
            }
        }

        return hits;
    }
}

public sealed record SearchHit(string Path, int Line, string Preview);

public sealed class SearchToolDescriptor : IToolWindowDescriptor
{
    public string Id => "search.results";
    public string Title => "Search";
    public string DefaultLocation => "Bottom";

    public object CreateContent(IServiceProvider services) =>
        new Avalonia.Controls.TextBlock
        {
            Name = "SearchToolContent",
            Text = "Find in Files — use command workbench.action.findInFiles",
            Margin = new Avalonia.Thickness(8)
        };
}
