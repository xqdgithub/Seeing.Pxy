using System.Net.Sockets;
using System.Threading.Channels;

namespace Seeing.Pxy.Client.Tunnel;

public sealed class LocalStreamSession
{
    public string StreamId { get; init; } = string.Empty;

    public TcpClient TcpClient { get; init; } = null!;

    public NetworkStream Stream { get; init; } = null!;

    public Channel<byte[]> Buffer { get; } = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.Wait });

    public CancellationTokenSource Cts { get; } = new();
}
