using Avalonia.Controls;
using Avalonia.Media;
using Edit.ComponentModel;

namespace Edit.Plugins.Sample;

[Plugin("edit.sample", "Sample Tool", "1.0.0")]
[ExportToolWindow("sample.tool", "Sample", DefaultLocation = "Right")]
public sealed class SampleToolPlugin : IPlugin
{
    public string Id => "edit.sample";
    public string Name => "Sample Tool";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        context.ToolWindows.Register(new SampleToolDescriptor());
        context.Commands.Register(new DelegateCommand(
            "sample.hello",
            "Sample: Hello",
            _ => Task.CompletedTask));
        context.Lsp.RegisterMethod(new DelegateLspMethodHandler(
            "edit/samplePing",
            isRequest: true,
            (_, _) => Task.FromResult<object?>("pong")));
        context.Lsp.RegisterCommand(new DelegateLspCommandHandler(
            "edit.sample.command",
            (_, _) => Task.FromResult<object?>(null)));
        return Task.CompletedTask;
    }
}

public sealed class SampleToolDescriptor : IToolWindowDescriptor
{
    public string Id => "sample.tool";
    public string Title => "Sample";
    public string DefaultLocation => "Right";

    public object CreateContent(IServiceProvider services) =>
        new Border
        {
            Name = "SampleToolContent",
            Background = Brushes.DimGray,
            Child = new TextBlock
            {
                Text = "Sample plugin tool window",
                Margin = new Avalonia.Thickness(12),
                Foreground = Brushes.White
            }
        };
}
