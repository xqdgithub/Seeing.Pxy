using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Seeing.Pxy.Server.Config;
using Seeing.Pxy.Server.Security;

namespace Seeing.Pxy.Tests.Security;

public sealed class ServerCertificateProviderTests
{
    [Fact]
    public void GetCurrentCertificate_HttpsDisabled_ReturnsPlaceholder()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            store.Config.EnableHttps = false;
            var provider = new ServerCertificateProvider(store);

            var cert = provider.GetCurrentCertificate();

            Assert.NotNull(cert);
            Assert.True(cert.HasPrivateKey);
            Assert.Contains("Placeholder", cert.SubjectName.Name, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task GetCurrentCertificate_EnabledWithValidPfx_ReturnsInstalledCertificate()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            store.Config.EnableHttps = true;
            var manager = new CertificateManager(store);
            var password = "cert-password";
            using var pfx = CreatePfx(password);
            var install = await manager.InstallAsync(pfx, "server.pfx", password);
            Assert.True(install.Success, install.Error);

            var provider = new ServerCertificateProvider(store);
            var cert = provider.GetCurrentCertificate();

            Assert.NotNull(cert);
            Assert.Equal("CN=Seeing.Pxy Test", cert.SubjectName.Name);
            using var expected = X509CertificateLoader.LoadPkcs12FromFile(store.Config.CertificatePath, password);
            Assert.Equal(expected.Thumbprint, cert.Thumbprint);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void GetCurrentCertificate_EnabledButFileMissing_ReturnsPlaceholder()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            store.Config.EnableHttps = true;
            store.Config.CertificatePath = Path.Combine(root, "missing.pfx");
            var provider = new ServerCertificateProvider(store);

            var cert = provider.GetCurrentCertificate();

            Assert.Contains("Placeholder", cert.SubjectName.Name, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void GetCurrentCertificate_EnabledButInvalidFile_ReturnsPlaceholder()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            store.Config.EnableHttps = true;
            store.Config.CertificatePath = Path.Combine(root, "broken.pfx");
            File.WriteAllBytes(store.Config.CertificatePath, new byte[] { 1, 2, 3, 4 });
            var provider = new ServerCertificateProvider(store);

            var cert = provider.GetCurrentCertificate();

            Assert.Contains("Placeholder", cert.SubjectName.Name, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task GetCurrentCertificate_AfterInstall_HotSwapsToNewCertificate()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            store.Config.EnableHttps = true;
            var manager = new CertificateManager(store);
            var firstPassword = "first-password";
            using var firstPfx = CreatePfx(firstPassword);
            Assert.True((await manager.InstallAsync(firstPfx, "first.pfx", firstPassword)).Success);

            var provider = new ServerCertificateProvider(store);
            var first = provider.GetCurrentCertificate();
            var firstThumbprint = first.Thumbprint;

            var secondPassword = "second-password";
            using var secondPfx = CreatePfx(secondPassword);
            Assert.True((await manager.InstallAsync(secondPfx, "second.pfx", secondPassword)).Success);

            var second = provider.GetCurrentCertificate();

            Assert.NotEqual(firstThumbprint, second.Thumbprint);
            Assert.Equal(secondPassword, store.Config.CertificatePassword);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task GetCurrentCertificate_AfterInstall_WithSamePassword_HotSwapsToNewCertificate()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            store.Config.EnableHttps = true;
            var manager = new CertificateManager(store);
            var password = "same-password";
            using var firstPfx = CreatePfx(password);
            Assert.True((await manager.InstallAsync(firstPfx, "first.pfx", password)).Success);

            var provider = new ServerCertificateProvider(store);
            var first = provider.GetCurrentCertificate();
            var firstThumbprint = first.Thumbprint;

            using var secondPfx = CreatePfx(password);
            Assert.True((await manager.InstallAsync(secondPfx, "second.pfx", password)).Success);

            var second = provider.GetCurrentCertificate();

            Assert.NotEqual(firstThumbprint, second.Thumbprint);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task GetCurrentCertificate_UnchangedFile_ReturnsCachedInstance()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            store.Config.EnableHttps = true;
            var password = "cert-password";
            using var pfx = CreatePfx(password);
            Assert.True((await new CertificateManager(store).InstallAsync(pfx, "server.pfx", password)).Success);

            var provider = new ServerCertificateProvider(store);
            var first = provider.GetCurrentCertificate();
            var second = provider.GetCurrentCertificate();

            Assert.Same(first, second);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task TryGetCertificate_WhenHttpsDisabled_StillReturnsInstalledCertificate()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            var password = "cert-password";
            using var pfx = CreatePfx(password);
            Assert.True((await new CertificateManager(store).InstallAsync(pfx, "server.pfx", password)).Success);
            store.Config.EnableHttps = false;

            var provider = new ServerCertificateProvider(store);
            var cert = provider.TryGetCertificate();

            Assert.NotNull(cert);
            Assert.Equal("CN=Seeing.Pxy Test", cert!.SubjectName.Name);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void TryGetCertificate_WhenNoCertificate_ReturnsNull()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            store.Config.CertificatePath = Path.Combine(root, "missing.pfx");
            var provider = new ServerCertificateProvider(store);

            var cert = provider.TryGetCertificate();

            Assert.Null(cert);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static MemoryStream CreatePfx(string password)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Seeing.Pxy Test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") },
                critical: false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        return new MemoryStream(certificate.Export(X509ContentType.Pkcs12, password));
    }

    private static string CreateRoot() => Path.Combine(Path.GetTempPath(), "seeing-pxy-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
