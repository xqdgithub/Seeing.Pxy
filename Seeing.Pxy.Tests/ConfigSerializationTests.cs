using System.Text.Json;
using Seeing.Pxy.Server.Config;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Tests;

public class ConfigSerializationTests
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [Fact]
    public void ForwardRule_Clone_Preserves_Values()
    {
        var rule = new ForwardRule { RemotePort = 8080, LocalHost = "192.168.1.10", LocalPort = 3389, Enabled = true };
        var clone = rule.Clone();

        Assert.Equal(rule.RemotePort, clone.RemotePort);
        Assert.Equal(rule.LocalHost, clone.LocalHost);
        Assert.Equal(rule.LocalPort, clone.LocalPort);
        Assert.Equal(rule.Enabled, clone.Enabled);
        Assert.Equal(rule.Id, clone.Id);
    }

    [Fact]
    public void ClientConfig_RoundTrip_Preserves_Rules()
    {
        var config = new ClientConfig
        {
            ServerUrl = "http://example.com:5000",
            Token = "secret",
            ClientName = "home-pc",
            Rules =
            {
                new ForwardRule { RemotePort = 7000, LocalHost = "127.0.0.1", LocalPort = 22 },
            },
        };

        var json = JsonSerializer.Serialize(config, Json);
        var back = JsonSerializer.Deserialize<ClientConfig>(json);

        Assert.NotNull(back);
        Assert.Equal("http://example.com:5000", back.ServerUrl);
        Assert.Equal("secret", back.Token);
        Assert.Equal("home-pc", back.ClientName);
        Assert.Single(back.Rules);
        Assert.Equal(7000, back.Rules[0].RemotePort);
    }

    [Fact]
    public void ServerConfig_RoundTrip_Preserves_Tokens()
    {
        var config = new ServerConfig { Tokens = { "token-a", "token-b" }, MinAllowedPort = 10000, MaxAllowedPort = 20000 };

        var json = JsonSerializer.Serialize(config, Json);
        var back = JsonSerializer.Deserialize<ServerConfig>(json);

        Assert.NotNull(back);
        Assert.Equal(2, back.Tokens.Count);
        Assert.Equal(10000, back.MinAllowedPort);
        Assert.Equal(20000, back.MaxAllowedPort);
    }

    [Fact]
    public void ServerConfigStore_Uses_User_Data_Directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "seeing-pxy-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new ServerConfigStore(root);

            Assert.Equal(Path.Combine(root, "server.json"), store.ConfigPath);
            Assert.Equal(Path.Combine(root, "https.pfx"), store.Config.CertificatePath);
            Assert.True(File.Exists(store.ConfigPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
