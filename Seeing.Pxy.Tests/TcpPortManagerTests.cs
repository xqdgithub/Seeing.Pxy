using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Seeing.Pxy.Server.Config;
using Seeing.Pxy.Server.Security;
using Seeing.Pxy.Server.Tunnel;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Tests;

public class TcpPortManagerTests
{
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static TcpPortManager CreateManager(string root) =>
        new(NullLogger<TcpPortManager>.Instance, new ServerCertificateProvider(new ServerConfigStore(root)));

    private static string CreateRoot() => Path.Combine(Path.GetTempPath(), "seeing-pxy-tests", Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryBindAsync_Binds_And_Unbinds()
    {
        var root = CreateRoot();
        try
        {
            var manager = CreateManager(root);
            var port = GetFreePort();
            var rule = new ForwardRule { Id = "r1", RemotePort = port };

            var (ok, error) = await manager.TryBindAsync("c1", rule, "127.0.0.1", false, (_, _, _) => Task.CompletedTask);

            Assert.True(ok, error);
            await manager.UnbindClientAsync("c1");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task TryBindAsync_Rejects_Duplicate_Port()
    {
        var root = CreateRoot();
        try
        {
            var manager = CreateManager(root);
            var port = GetFreePort();

            var rule1 = new ForwardRule { Id = "r1", RemotePort = port };
            var rule2 = new ForwardRule { Id = "r2", RemotePort = port };

            var (ok1, _) = await manager.TryBindAsync("c1", rule1, "127.0.0.1", false, (_, _, _) => Task.CompletedTask);
            var (ok2, error2) = await manager.TryBindAsync("c2", rule2, "127.0.0.1", false, (_, _, _) => Task.CompletedTask);

            Assert.True(ok1);
            Assert.False(ok2);
            Assert.NotNull(error2);

            await manager.UnbindClientAsync("c1");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task UnbindClientAsync_Releases_Port()
    {
        var root = CreateRoot();
        try
        {
            var manager = CreateManager(root);
            var port = GetFreePort();
            var rule = new ForwardRule { Id = "r1", RemotePort = port };

            await manager.TryBindAsync("c1", rule, "127.0.0.1", false, (_, _, _) => Task.CompletedTask);
            await manager.UnbindClientAsync("c1");

            var rule2 = new ForwardRule { Id = "r2", RemotePort = port };
            var (ok2, error2) = await manager.TryBindAsync("c2", rule2, "127.0.0.1", false, (_, _, _) => Task.CompletedTask);

            Assert.True(ok2, error2);
            await manager.UnbindClientAsync("c2");
        }
        finally
        {
            DeleteRoot(root);
        }
    }
}
