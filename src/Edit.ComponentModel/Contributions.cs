namespace Edit.ComponentModel;

[AttributeUsage(AttributeTargets.Class)]
public sealed class PluginAttribute : Attribute
{
    public PluginAttribute(string id, string name, string version = "1.0.0")
    {
        Id = id;
        Name = name;
        Version = version;
    }

    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ExportCommandAttribute : Attribute
{
    public ExportCommandAttribute(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }
    public string Title { get; }
    public string? KeyGesture { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ExportToolWindowAttribute : Attribute
{
    public ExportToolWindowAttribute(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }
    public string Title { get; }
    public string DefaultLocation { get; set; } = "Left";
}

public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    Task InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default);
}

public interface IPluginContext
{
    IServiceProvider Services { get; }
    ICommandRegistry Commands { get; }
    IToolWindowRegistry ToolWindows { get; }
}

public interface ICommand
{
    string Id { get; }
    string Title { get; }
    string? KeyGesture { get; }
    Task ExecuteAsync(CancellationToken cancellationToken = default);
    bool CanExecute();
}

public interface ICommandRegistry
{
    void Register(ICommand command);
    ICommand? Get(string id);
    IReadOnlyList<ICommand> All { get; }
    Task ExecuteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IToolWindowDescriptor
{
    string Id { get; }
    string Title { get; }
    string DefaultLocation { get; }
    object CreateContent(IServiceProvider services);
}

public interface IToolWindowRegistry
{
    void Register(IToolWindowDescriptor descriptor);
    IReadOnlyList<IToolWindowDescriptor> All { get; }
}

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ICommand> All => _commands.Values.ToList();

    public void Register(ICommand command) => _commands[command.Id] = command;

    public ICommand? Get(string id) => _commands.TryGetValue(id, out var c) ? c : null;

    public Task ExecuteAsync(string id, CancellationToken cancellationToken = default)
    {
        var cmd = Get(id) ?? throw new InvalidOperationException($"Unknown command: {id}");
        if (!cmd.CanExecute()) return Task.CompletedTask;
        return cmd.ExecuteAsync(cancellationToken);
    }
}

public sealed class ToolWindowRegistry : IToolWindowRegistry
{
    private readonly List<IToolWindowDescriptor> _tools = new();
    public IReadOnlyList<IToolWindowDescriptor> All => _tools;
    public void Register(IToolWindowDescriptor descriptor) => _tools.Add(descriptor);
}

public sealed class DelegateCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;

    public DelegateCommand(string id, string title, Func<CancellationToken, Task> execute, string? keyGesture = null, Func<bool>? canExecute = null)
    {
        Id = id;
        Title = title;
        KeyGesture = keyGesture;
        _execute = execute;
        _canExecute = canExecute;
    }

    public string Id { get; }
    public string Title { get; }
    public string? KeyGesture { get; }
    public bool CanExecute() => _canExecute?.Invoke() ?? true;
    public Task ExecuteAsync(CancellationToken cancellationToken = default) => _execute(cancellationToken);
}
