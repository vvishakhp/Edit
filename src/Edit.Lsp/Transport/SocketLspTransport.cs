using System.Diagnostics;
using System.Net.Sockets;

namespace Edit.Lsp.Transport;

public sealed class SocketLspTransport : ILspTransport
{
    private readonly TcpClient _client;

    public SocketLspTransport(TcpClient client)
    {
        _client = client;
        var stream = client.GetStream();
        SendingStream = stream;
        ReceivingStream = stream;
        Description = $"socket:{client.Client.RemoteEndPoint}";
    }

    public Stream SendingStream { get; }
    public Stream ReceivingStream { get; }
    public string Description { get; }

    public static async Task<SocketLspTransport> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);
        return new SocketLspTransport(client);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
