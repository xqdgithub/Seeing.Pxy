using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Seeing.Pxy.Server.Components;
using Seeing.Pxy.Server.Config;
using Seeing.Pxy.Server.Tunnel;
using Seeing.Pxy.Shared;

var builder = WebApplication.CreateBuilder(args);

var configStore = new ServerConfigStore(builder.Environment);

builder.Services.AddSingleton(configStore);
builder.Services.AddSingleton<TcpPortManager>();
builder.Services.AddSingleton<TunnelService>();

builder.WebHost.ConfigureKestrel(options =>
{
    var config = configStore.Config;
    var host = ResolveListenAddress(config.ListenHost);

    options.Listen(host, config.ManagementPort);

    if (config.EnableHttps)
    {
        try
        {
            var cert = TryLoadCertificate(config);
            if (cert is not null)
            {
                options.Listen(host, config.HttpsPort, listen => listen.UseHttps(cert));
            }
            else
            {
                options.Listen(host, config.HttpsPort, listen => listen.UseHttps());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Seeing.Pxy] HTTPS 端点启用失败：{ex.Message}，仅监听 HTTP {config.ManagementPort}");
        }
    }
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAntDesign();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<TunnelHub>("/tunnel");

app.Run();

static IPAddress ResolveListenAddress(string listenHost)
{
    if (string.IsNullOrWhiteSpace(listenHost) || listenHost is "0.0.0.0" or "*")
    {
        return IPAddress.IPv6Any;
    }

    return IPAddress.TryParse(listenHost, out var addr) ? addr : IPAddress.IPv6Any;
}

static X509Certificate2? TryLoadCertificate(ServerConfig config)
{
    if (string.IsNullOrWhiteSpace(config.CertificatePath) || !File.Exists(config.CertificatePath))
    {
        return null;
    }

    try
    {
        return X509CertificateLoader.LoadPkcs12FromFile(config.CertificatePath, config.CertificatePassword);
    }
    catch
    {
        return null;
    }
}
