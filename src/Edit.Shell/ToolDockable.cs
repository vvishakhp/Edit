using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;

namespace Edit.Shell;

/// <summary>
/// Dock tool that implements <see cref="IToolContent"/> and <see cref="IRecyclingDataTemplate"/>
/// so Fluent themes render the hosted control.
/// </summary>
public sealed class ToolDockable : Tool, IToolContent, IRecyclingDataTemplate
{
    public ToolDockable(string id, string title, Control content)
    {
        Id = id;
        Title = title;
        ToolContent = content;
        Content = content;
        Context = content;
    }

    public Control ToolContent { get; }

    /// <inheritdoc />
    public object? Content { get; set; }

    public bool Match(object? data) => data is ToolDockable;

    public Control? Build(object? data) => Build(data, null);

    public Control? Build(object? data, Control? existing)
    {
        if (existing is not null)
            return existing;
        return Content as Control ?? ToolContent;
    }
}
