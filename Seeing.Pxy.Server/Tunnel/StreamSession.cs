using System.Net.Sockets;
using System.Threading.Channels;

namespace Seeing.Pxy.Server.Tunnel;

public sealed class StreamSession
{
    public string StreamId { get; init; } = string.Empty;

    public string ClientName { get; init; } = string.Empty;

    public string RuleId { get; init; } = string.Empty;

    public string LocalHost { get; init; } = string.Empty;

    public int LocalPort { get; init; }

    public string ConnectionId { get; init; } = string.Empty;

    public Socket Socket { get; init; } = null!;

    public NetworkStream Stream { get; init; } = null!;

    public Channel<byte[]> Buffer { get; } = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.Wait });

    public CancellationTokenSource Cts { get; } = new();
}
