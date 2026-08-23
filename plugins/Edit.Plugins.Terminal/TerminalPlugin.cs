using Edit.ComponentModel;

namespace Edit.Plugins.Terminal;

[Plugin("edit.terminal", "Terminal", "1.0.0")]
[ExportToolWindow("terminal.panel", "Terminal", DefaultLocation = "Bottom")]
public sealed class TerminalPlugin : IPlugin
{
    public string Id => "edit.terminal";
    public string Name => "Terminal";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        context.ToolWindows.Register(new TerminalToolDescriptor());
        context.Commands.Register(new DelegateCommand(
            "workbench.action.terminal.toggle",
            "Toggle Terminal",
            _ => Task.CompletedTask,
            "Ctrl+`"));
        return Task.CompletedTask;
    }
}

/// <summary>Lightweight process runner until a full PTY integration lands.</summary>
public sealed class TerminalSession
{
    public string LastOutput { get; private set; } = "";

    public async Task<string> RunAsync(string fileName, string args, string? workingDirectory = null, CancellationToken ct = default)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        var stderr = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        LastOutput = string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + "\n" + stderr;
        return LastOutput;
    }
}

public sealed class TerminalToolDescriptor : IToolWindowDescriptor
{
    public string Id => "terminal.panel";
    public string Title => "Terminal";
    public string DefaultLocation => "Bottom";

    public object CreateContent(IServiceProvider services) =>
        new Avalonia.Controls.TextBlock
        {
            Name = "TerminalToolContent",
            Text = "Terminal panel (process runner MVP; full PTY later)",
            Margin = new Avalonia.Thickness(8)
        };
}
