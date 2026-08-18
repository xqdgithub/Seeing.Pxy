namespace Seeing.Pxy.Shared;

public class ServerConfig
{
    public string ListenHost { get; set; } = "0.0.0.0";

    public int ManagementPort { get; set; } = 5000;

    public List<string> Tokens { get; set; } = new();

    public int MinAllowedPort { get; set; } = 10000;

    public int MaxAllowedPort { get; set; } = 60000;
}

public class ClientConfig
{
    public string ServerUrl { get; set; } = "http://localhost:5000";

    public string Token { get; set; } = string.Empty;

    public string ClientName { get; set; } = "default";

    public List<ForwardRule> Rules { get; set; } = new();
}
