namespace Seeing.Pxy.Shared;

public class ServerConfig
{
    public string ListenHost { get; set; } = "0.0.0.0";

    public int ManagementPort { get; set; } = 6001;

    public List<string> Tokens { get; set; } = new();

    public int MinAllowedPort { get; set; } = 6100;

    public int MaxAllowedPort { get; set; } = 6200;
}

public class ClientConfig
{
    public string ServerUrl { get; set; } = "http://localhost:6001";

    public string Token { get; set; } = string.Empty;

    public string ClientName { get; set; } = "default";

    public List<ForwardRule> Rules { get; set; } = new();
}
