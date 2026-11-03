using System.Diagnostics;

namespace Edit.Lsp.Transport;

public interface ILspTransport : IAsyncDisposable
{
    Stream SendingStream { get; }
    Stream ReceivingStream { get; }
    string Description { get; }
}

public sealed class StdioLspTransport : ILspTransport
{
    private readonly Process _process;

    public StdioLspTransport(Process process)
    {
        _process = process;
        SendingStream = process.StandardInput.BaseStream;
        ReceivingStream = process.StandardOutput.BaseStream;
        Description = $"stdio:{process.Id}";
    }

    public Stream SendingStream { get; }
    public Stream ReceivingStream { get; }
    public string Description { get; }

    public static StdioLspTransport Start(string command, IEnumerable<string> args, string workingDirectory)
    {
        var resolved = ResolveCommand(command);
        if (resolved is null)
            throw new FileNotFoundException(
                $"Language server command not found on PATH (also checked GOPATH/bin, ~/go/bin): {command}",
                command);

        var psi = new ProcessStartInfo
        {
            FileName = resolved,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start language server: {command}");

        // Drain stderr so the process cannot block on a full pipe.
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        Debug.WriteLine($"[{command}] {line}");
                }
            }
            catch
            {
                // ignore
            }
        });

        return new StdioLspTransport(process);
    }

    public static string? ResolveCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        if (Path.IsPathRooted(command) && File.Exists(command)) return command;
        if (File.Exists(command)) return Path.GetFullPath(command);

        foreach (var dir in EnumerateSearchDirectories())
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate)) return candidate;
            if (OperatingSystem.IsWindows())
            {
                foreach (var ext in new[] { ".exe", ".cmd", ".bat" })
                {
                    var withExt = candidate + ext;
                    if (File.Exists(withExt)) return withExt;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSearchDirectories()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try
            {
                var full = Path.GetFullPath(dir);
                if (Directory.Exists(full))
                    seen.Add(full);
            }
            catch
            {
                // ignore invalid paths
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            Add(part);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(Path.Combine(home, "go", "bin"));
        Add(Path.Combine(home, ".local", "bin"));
        Add(Path.Combine(home, ".cargo", "bin"));

        var goPath = Environment.GetEnvironmentVariable("GOPATH");
        if (!string.IsNullOrWhiteSpace(goPath))
            Add(Path.Combine(goPath, "bin"));

        var goBin = Environment.GetEnvironmentVariable("GOBIN");
        Add(goBin);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "go",
                ArgumentList = { "env", "GOPATH" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is not null)
            {
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(2000);
                if (!string.IsNullOrWhiteSpace(output))
                    Add(Path.Combine(output, "bin"));
            }
        }
        catch
        {
            // go not available
        }

        Add("/usr/bin");
        Add("/usr/local/bin");

        return seen;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}
