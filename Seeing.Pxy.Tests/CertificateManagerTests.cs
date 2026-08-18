using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Seeing.Pxy.Server.Config;
using Seeing.Pxy.Server.Security;

namespace Seeing.Pxy.Tests;

public sealed class CertificateManagerTests
{
    [Fact]
    public async Task InstallAsync_Stores_Valid_Pfx_And_Password()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            var manager = new CertificateManager(store);
            var password = "new-password";
            await using var pfx = CreatePfx(password);

            var result = await manager.InstallAsync(pfx, "server.pfx", password);

            Assert.True(result.Success, result.Error);
            Assert.Equal(Path.Combine(root, "https.pfx"), store.Config.CertificatePath);
            Assert.Equal(password, store.Config.CertificatePassword);
            Assert.True(File.Exists(store.Config.CertificatePath));

            using var installed = X509CertificateLoader.LoadPkcs12FromFile(store.Config.CertificatePath, password);
            Assert.True(installed.HasPrivateKey);
            Assert.Contains(installed.Extensions.OfType<X509EnhancedKeyUsageExtension>(), IsServerAuthentication);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task InstallAsync_Invalid_Pfx_Does_Not_Replace_Existing_Certificate()
    {
        var root = CreateRoot();
        try
        {
            var store = new ServerConfigStore(root);
            var manager = new CertificateManager(store);
            var oldPassword = "old-password";
            await using var oldPfx = CreatePfx(oldPassword);
            var oldResult = await manager.InstallAsync(oldPfx, "old.pfx", oldPassword);
            Assert.True(oldResult.Success, oldResult.Error);
            var oldThumbprint = X509CertificateLoader.LoadPkcs12FromFile(store.Config.CertificatePath, oldPassword).Thumbprint;

            await using var invalidPfx = CreatePfx("actual-password");
            var result = await manager.InstallAsync(invalidPfx, "replacement.pfx", "wrong-password");

            Assert.False(result.Success);
            Assert.Equal(oldPassword, store.Config.CertificatePassword);
            using var current = X509CertificateLoader.LoadPkcs12FromFile(store.Config.CertificatePath, oldPassword);
            Assert.Equal(oldThumbprint, current.Thumbprint);
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

    private static bool IsServerAuthentication(X509EnhancedKeyUsageExtension extension) =>
        extension.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.1");

    private static string CreateRoot() => Path.Combine(Path.GetTempPath(), "seeing-pxy-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
