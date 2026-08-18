using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Seeing.Pxy.Server.Config;

namespace Seeing.Pxy.Server.Security;

public sealed class ServerCertificateProvider
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string PlaceholderSubject = "CN=Seeing.Pxy Placeholder";

    private readonly ServerConfigStore _configStore;
    private readonly X509Certificate2 _placeholder;
    private readonly object _sync = new();

    private X509Certificate2? _cachedCertificate;
    private string? _cachedPath;
    private string _cachedPassword = string.Empty;
    private long _cachedLength;
    private DateTime _cachedLastWrite;

    public ServerCertificateProvider(ServerConfigStore configStore)
    {
        _configStore = configStore;
        _placeholder = CreatePlaceholderCertificate();
    }

    public X509Certificate2 GetCurrentCertificate()
    {
        lock (_sync)
        {
            if (!_configStore.Config.EnableHttps)
            {
                return _placeholder;
            }

            return TryGetCertificateCore() ?? _placeholder;
        }
    }

    public X509Certificate2? TryGetCertificate()
    {
        lock (_sync)
        {
            return TryGetCertificateCore();
        }
    }

    private X509Certificate2? TryGetCertificateCore()
    {
        var config = _configStore.Config;
        if (string.IsNullOrWhiteSpace(config.CertificatePath))
        {
            return null;
        }

        FileInfo? file;
        try
        {
            file = new FileInfo(config.CertificatePath);
            if (!file.Exists)
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        if (_cachedCertificate is not null &&
            _cachedPath == config.CertificatePath &&
            _cachedPassword == config.CertificatePassword &&
            _cachedLength == file.Length &&
            _cachedLastWrite == file.LastWriteTimeUtc)
        {
            return _cachedCertificate;
        }

        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(file.FullName, config.CertificatePassword);
            // 旧证书实例交由 GC 释放：Kestrel 握手可能仍在使用，主动 Dispose 会破坏在途 TLS 握手。
            _cachedCertificate = certificate;
            _cachedPath = config.CertificatePath;
            _cachedPassword = config.CertificatePassword;
            _cachedLength = file.Length;
            _cachedLastWrite = file.LastWriteTimeUtc;
            return certificate;
        }
        catch
        {
            return null;
        }
    }

    private static X509Certificate2 CreatePlaceholderCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            PlaceholderSubject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new(ServerAuthenticationOid) },
                critical: false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(10));
    }
}
