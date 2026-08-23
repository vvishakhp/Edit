namespace Edit.Workspace;

public sealed class WorkspaceFolder
{
    public WorkspaceFolder(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Name = System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
    }

    public string Path { get; }
    public string Name { get; }
}

public sealed class FileNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public List<FileNode> Children { get; init; } = new();
}

public sealed class WorkspaceService : IDisposable
{
    private FileSystemWatcher? _watcher;

    public WorkspaceFolder? Folder { get; private set; }
    public event EventHandler? Changed;

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);

        _watcher?.Dispose();
        Folder = new WorkspaceFolder(path);
        _watcher = new FileSystemWatcher(Folder.Path)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.Created += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.Deleted += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _watcher.Renamed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<FileNode> GetTree(int maxDepth = 4)
    {
        if (Folder is null) return Array.Empty<FileNode>();
        return BuildTree(Folder.Path, 0, maxDepth);
    }

    private static List<FileNode> BuildTree(string path, int depth, int maxDepth)
    {
        var nodes = new List<FileNode>();
        if (depth > maxDepth) return nodes;

        try
        {
            foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = System.IO.Path.GetFileName(dir);
                if (name is "bin" or "obj" or ".git" or "node_modules") continue;
                nodes.Add(new FileNode
                {
                    Name = name,
                    FullPath = dir,
                    IsDirectory = true,
                    Children = BuildTree(dir, depth + 1, maxDepth)
                });
            }

            foreach (var file in Directory.GetFiles(path).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                nodes.Add(new FileNode
                {
                    Name = System.IO.Path.GetFileName(file),
                    FullPath = file,
                    IsDirectory = false
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            // ignore
        }

        return nodes;
    }

    public void Dispose() => _watcher?.Dispose();
}
