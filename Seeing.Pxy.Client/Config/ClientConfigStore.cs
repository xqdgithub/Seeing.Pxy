using System.Text.Json;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Client.Config;

public sealed class ClientConfigStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public ClientConfig Config { get; private set; } = new();

    public ClientConfigStore(IWebHostEnvironment env)
    {
        _path = Path.Combine(env.ContentRootPath, "client.json");
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            Config = new ClientConfig();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            Config = JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
        }
        catch
        {
            Config = new ClientConfig();
        }
    }

    public void Save(ClientConfig? config = null)
    {
        if (config is not null)
        {
            Config = config;
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(Config, _json));
    }
}
