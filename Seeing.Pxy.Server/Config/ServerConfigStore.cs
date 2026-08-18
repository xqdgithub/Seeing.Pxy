using System.Text.Json;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Server.Config;

public sealed class ServerConfigStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public ServerConfig Config { get; private set; } = new();

    public ServerConfigStore(IWebHostEnvironment env)
    {
        _path = Path.Combine(env.ContentRootPath, "server.json");
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            Config = new ServerConfig();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            Config = JsonSerializer.Deserialize<ServerConfig>(json) ?? new ServerConfig();
        }
        catch
        {
            Config = new ServerConfig();
        }
    }

    public void Save(ServerConfig? config = null)
    {
        if (config is not null)
        {
            Config = config;
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(Config, _json));
    }
}
