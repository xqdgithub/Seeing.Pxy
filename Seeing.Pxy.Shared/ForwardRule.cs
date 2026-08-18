namespace Seeing.Pxy.Shared;

public class ForwardRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public int RemotePort { get; set; }

    public string LocalHost { get; set; } = "127.0.0.1";

    public int LocalPort { get; set; }

    public bool Enabled { get; set; } = true;

    public ForwardRule Clone() => new()
    {
        Id = Id,
        RemotePort = RemotePort,
        LocalHost = LocalHost,
        LocalPort = LocalPort,
        Enabled = Enabled,
    };
}
