namespace Seeing.Pxy.Server.Tunnel;

public interface ITunnelClient
{
    Task NewConnection(string streamId, string localHost, int localPort);

    Task SendData(string streamId, byte[] data);

    Task CloseStream(string streamId);
}
