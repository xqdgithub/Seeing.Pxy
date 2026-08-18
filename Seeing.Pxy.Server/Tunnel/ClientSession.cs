using System.Collections.Concurrent;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Server.Tunnel;

public sealed class ClientSession
{
    public string ClientName { get; init; } = string.Empty;

    public string ConnectionId { get; init; } = string.Empty;

    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;

    public ConcurrentDictionary<string, ForwardRule> Rules { get; } = new();
}
