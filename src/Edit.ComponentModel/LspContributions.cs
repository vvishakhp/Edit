namespace Edit.ComponentModel;

/// <summary>Handles a custom LSP method (server→client request or notification).</summary>
public interface ILspMethodHandler
{
    string Method { get; }
    bool IsRequest { get; }
    Task<object?> HandleAsync(object? @params, CancellationToken cancellationToken = default);
}

/// <summary>Handles <c>workspace/executeCommand</c> for a specific command id.</summary>
public interface ILspCommandHandler
{
    string CommandId { get; }
    Task<object?> ExecuteAsync(object?[]? arguments, CancellationToken cancellationToken = default);
}

public interface ILspContributionRegistry
{
    void RegisterMethod(ILspMethodHandler handler);
    void RegisterCommand(ILspCommandHandler handler);
    ILspMethodHandler? GetMethod(string method);
    ILspCommandHandler? GetCommand(string commandId);
    IReadOnlyList<ILspMethodHandler> Methods { get; }
    IReadOnlyList<ILspCommandHandler> Commands { get; }
}

public sealed class LspContributionRegistry : ILspContributionRegistry
{
    private readonly Dictionary<string, ILspMethodHandler> _methods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ILspCommandHandler> _commands = new(StringComparer.Ordinal);

    public void RegisterMethod(ILspMethodHandler handler) => _methods[handler.Method] = handler;
    public void RegisterCommand(ILspCommandHandler handler) => _commands[handler.CommandId] = handler;
    public ILspMethodHandler? GetMethod(string method) =>
        _methods.TryGetValue(method, out var h) ? h : null;
    public ILspCommandHandler? GetCommand(string commandId) =>
        _commands.TryGetValue(commandId, out var h) ? h : null;
    public IReadOnlyList<ILspMethodHandler> Methods => _methods.Values.ToList();
    public IReadOnlyList<ILspCommandHandler> Commands => _commands.Values.ToList();
}

public sealed class DelegateLspMethodHandler : ILspMethodHandler
{
    private readonly Func<object?, CancellationToken, Task<object?>> _handler;

    public DelegateLspMethodHandler(string method, bool isRequest, Func<object?, CancellationToken, Task<object?>> handler)
    {
        Method = method;
        IsRequest = isRequest;
        _handler = handler;
    }

    public string Method { get; }
    public bool IsRequest { get; }
    public Task<object?> HandleAsync(object? @params, CancellationToken cancellationToken = default) =>
        _handler(@params, cancellationToken);
}

public sealed class DelegateLspCommandHandler : ILspCommandHandler
{
    private readonly Func<object?[]?, CancellationToken, Task<object?>> _handler;

    public DelegateLspCommandHandler(string commandId, Func<object?[]?, CancellationToken, Task<object?>> handler)
    {
        CommandId = commandId;
        _handler = handler;
    }

    public string CommandId { get; }
    public Task<object?> ExecuteAsync(object?[]? arguments, CancellationToken cancellationToken = default) =>
        _handler(arguments, cancellationToken);
}
