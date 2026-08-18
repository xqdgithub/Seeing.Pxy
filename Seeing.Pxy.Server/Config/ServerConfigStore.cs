using System.Text.Json;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Server.Config;

public sealed class ServerConfigStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public ServerConfig Config { get; private set; } = new();

    public string ConfigPath => _path;

    public ServerConfigStore(IWebHostEnvironment env)
    {
        _path = GetUserConfigPath();
        MigrateLegacyConfig(env);
        Load();
    }

    public ServerConfigStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "server.json");
        Load();
    }

    private void MigrateLegacyConfig(IWebHostEnvironment env)
    {
        var legacyPath = Path.Combine(env.ContentRootPath, "server.json");
        if (!File.Exists(legacyPath) || File.Exists(_path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.Copy(legacyPath, _path, overwrite: false);
            Console.WriteLine($"[Seeing.Pxy] 已从旧位置迁移配置：{legacyPath} -> {_path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Seeing.Pxy] 迁移旧配置失败：{ex.Message}");
        }
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            Config = new ServerConfig();
            Config.CertificatePath = Path.Combine(Path.GetDirectoryName(_path)!, "https.pfx");
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

        if (string.IsNullOrWhiteSpace(Config.CertificatePath))
        {
            Config.CertificatePath = Path.Combine(Path.GetDirectoryName(_path)!, "https.pfx");
        }
    }

    public void Save(ServerConfig? config = null)
    {
        if (config is not null)
        {
            Config = config;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Config, _json));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private static string GetUserConfigPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".seeing", "pxy", "server.json");
    }
}
