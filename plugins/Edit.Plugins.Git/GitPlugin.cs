using System.Diagnostics;
using Edit.ComponentModel;

namespace Edit.Plugins.Git;

[Plugin("edit.git", "Git", "1.0.0")]
[ExportToolWindow("git.status", "Git", DefaultLocation = "Left")]
public sealed class GitPlugin : IPlugin
{
    public string Id => "edit.git";
    public string Name => "Git";

    public Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        context.ToolWindows.Register(new GitToolDescriptor());
        context.Commands.Register(new DelegateCommand(
            "git.refresh",
            "Git: Refresh Status",
            _ => Task.CompletedTask));
        return Task.CompletedTask;
    }
}

public sealed class GitService
{
    public string StatusSummary(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return "No workspace";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --short --branch",
                WorkingDirectory = workspaceRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return "git unavailable";
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            return string.IsNullOrWhiteSpace(output) ? "Clean working tree" : output.Trim();
        }
        catch
        {
            return "git not available (install git or use later LibGit2Sharp plugin)";
        }
    }
}

public sealed class GitToolDescriptor : IToolWindowDescriptor
{
    public string Id => "git.status";
    public string Title => "Git";
    public string DefaultLocation => "Left";

    public object CreateContent(IServiceProvider services) =>
        new Avalonia.Controls.TextBlock
        {
            Name = "GitToolContent",
            Text = "Git status tool — open a folder and refresh",
            Margin = new Avalonia.Thickness(8),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
}
