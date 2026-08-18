using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Seeing.Pxy.Shared;
using Seeing.Pxy.Server.Config;

namespace Seeing.Pxy.Server.Security;

public sealed class CertificateManager
{
    private const long MaximumCertificateSize = 16 * 1024 * 1024;
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private readonly ServerConfigStore _configStore;

    public CertificateManager(ServerConfigStore configStore)
    {
        _configStore = configStore;
    }

    public async Task<CertificateInstallResult> InstallAsync(
        Stream content,
        string fileName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".pfx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetExtension(fileName), ".p12", StringComparison.OrdinalIgnoreCase))
        {
            return CertificateInstallResult.Failed("证书文件必须是 PFX 或 PKCS#12 格式");
        }

        var destination = _configStore.Config.CertificatePath;
        if (string.IsNullOrWhiteSpace(destination))
        {
            return CertificateInstallResult.Failed("证书保存路径未配置");
        }

        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return CertificateInstallResult.Failed("证书保存目录无效");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteTemporaryFileAsync(content, temporaryPath, cancellationToken).ConfigureAwait(false);
            using var certificate = LoadAndValidate(temporaryPath, password);

            var previousConfig = CloneConfig(_configStore.Config);
            var previousCertificate = File.Exists(destination) ? File.ReadAllBytes(destination) : null;
            File.Move(temporaryPath, destination, overwrite: true);

            try
            {
                _configStore.Save(new ServerConfig
                {
                    ListenHost = previousConfig.ListenHost,
                    ManagementPort = previousConfig.ManagementPort,
                    EnableHttps = previousConfig.EnableHttps,
                    HttpsPort = previousConfig.HttpsPort,
                    CertificatePath = destination,
                    CertificatePassword = password,
                    Tokens = new List<string>(previousConfig.Tokens),
                    MinAllowedPort = previousConfig.MinAllowedPort,
                    MaxAllowedPort = previousConfig.MaxAllowedPort,
                });
            }
            catch
            {
                RestoreCertificate(destination, previousCertificate);
                _configStore.Save(previousConfig);
                throw;
            }

            return CertificateInstallResult.Succeeded(certificate.Subject, certificate.NotAfter);
        }
        catch (CryptographicException ex)
        {
            return CertificateInstallResult.Failed($"证书校验失败：{ex.Message}");
        }
        catch (InvalidDataException ex)
        {
            return CertificateInstallResult.Failed(ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CertificateInstallResult.Failed($"证书安装失败：{ex.Message}");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task WriteTemporaryFileAsync(Stream content, string path, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaximumCertificateSize)
            {
                throw new InvalidDataException("证书文件不能超过 16 MB");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static X509Certificate2 LoadAndValidate(string path, string password)
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new CryptographicException("证书必须包含私钥");
        }

        var hasServerAuthentication = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .Any(extension => extension.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == ServerAuthenticationOid));
        if (!hasServerAuthentication)
        {
            certificate.Dispose();
            throw new CryptographicException("证书必须包含服务端 TLS 用途");
        }

        return certificate;
    }

    private static ServerConfig CloneConfig(ServerConfig config) => new()
    {
        ListenHost = config.ListenHost,
        ManagementPort = config.ManagementPort,
        EnableHttps = config.EnableHttps,
        HttpsPort = config.HttpsPort,
        CertificatePath = config.CertificatePath,
        CertificatePassword = config.CertificatePassword,
        Tokens = new List<string>(config.Tokens),
        MinAllowedPort = config.MinAllowedPort,
        MaxAllowedPort = config.MaxAllowedPort,
    };

    private static void RestoreCertificate(string destination, byte[]? previousCertificate)
    {
        if (previousCertificate is null)
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            return;
        }

        File.WriteAllBytes(destination, previousCertificate);
    }
}

public sealed record CertificateInstallResult(bool Success, string? Error, string? Subject, DateTime NotAfter)
{
    public static CertificateInstallResult Succeeded(string subject, DateTime notAfter) =>
        new(true, null, subject, notAfter);

    public static CertificateInstallResult Failed(string error) =>
        new(false, error, null, default);
}
