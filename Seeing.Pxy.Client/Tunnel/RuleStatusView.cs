using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Client.Tunnel;

public sealed class RuleStatusView
{
    public ForwardRule Rule { get; init; } = null!;

    public int ActiveConnections { get; init; }

    public string? Error { get; init; }
}
