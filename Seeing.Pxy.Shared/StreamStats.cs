namespace Seeing.Pxy.Shared;

public class StreamStats
{
    public long ConnectionCount { get; set; }

    public long InboundBytes { get; set; }

    public long OutboundBytes { get; set; }
}

public class RuleStatus
{
    public ForwardRule Rule { get; set; } = new();

    public string ClientName { get; set; } = string.Empty;

    public bool Listening { get; set; }

    public string? Error { get; set; }

    public StreamStats Stats { get; set; } = new();
}
