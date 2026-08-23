namespace Edit.Dap;

/// <summary>Reserved Debug Adapter Protocol client surface (post-MVP implementation).</summary>
public interface IDebugAdapterClient
{
    string Status { get; }
    Task StartAsync(DebugAdapterLaunchRequest request, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task ContinueAsync(CancellationToken cancellationToken = default);
    Task SetBreakpointsAsync(string path, IReadOnlyList<int> lines, CancellationToken cancellationToken = default);
}

public sealed class DebugAdapterLaunchRequest
{
    public required string AdapterCommand { get; init; }
    public List<string> Args { get; init; } = new();
    public string? WorkingDirectory { get; init; }
    public string? Program { get; init; }
}

public sealed class NoOpDebugAdapterClient : IDebugAdapterClient
{
    public string Status => "DAP: Off";
    public Task StartAsync(DebugAdapterLaunchRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetBreakpointsAsync(string path, IReadOnlyList<int> lines, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
