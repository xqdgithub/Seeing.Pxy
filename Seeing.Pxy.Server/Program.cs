using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Seeing.Pxy.Server.Components;
using Seeing.Pxy.Server.Config;
using Seeing.Pxy.Server.Security;
using Seeing.Pxy.Server.Tunnel;
using Seeing.Pxy.Shared;

var builder = WebApplication.CreateBuilder(args);

var configStore = new ServerConfigStore(builder.Environment);
var certificateProvider = new ServerCertificateProvider(configStore);

builder.Services.AddSingleton(configStore);
builder.Services.AddSingleton(certificateProvider);
builder.Services.AddSingleton<CertificateManager>();
builder.Services.AddSingleton<TcpPortManager>();
builder.Services.AddSingleton<TunnelService>();

var listenHost = ResolveListenAddress(configStore.Config.ListenHost);
var httpsPortAvailable = IsPortAvailable(listenHost, configStore.Config.HttpsPort);
if (!httpsPortAvailable)
{
    Console.WriteLine($"[Seeing.Pxy] HTTPS 端口 {listenHost}:{configStore.Config.HttpsPort} 已被占用，本次运行仅监听 HTTP {configStore.Config.ManagementPort}");
}

builder.WebHost.ConfigureKestrel(options =>
{
    var config = configStore.Config;

    options.Listen(listenHost, config.ManagementPort);

    if (!httpsPortAvailable)
    {
        return;
    }

    try
    {
        options.Listen(listenHost, config.HttpsPort, listen =>
        {
            listen.Use(async (connection, next) =>
            {
                byte[] prefix;
                try
                {
                    prefix = await ReadAvailableAsync(connection);
                }
                catch (OperationCanceledException)
                {
                    await next();
                    return;
                }

                if (prefix.Length == 0)
                {
                    await next();
                    return;
                }

                if (prefix[0] != 0x16)
                {
                    await WritePlainHttpHintAsync(connection);
                    return;
                }

                await FeedBackAndRunAsync(connection, prefix, next);
            });
            listen.UseHttps(https =>
            {
                https.ServerCertificateSelector = (context, name) => certificateProvider.GetCurrentCertificate();
            });
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seeing.Pxy] HTTPS 监听 {listenHost}:{config.HttpsPort} 启用失败：{ex.Message}，仅监听 HTTP {config.ManagementPort}");
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

const string DisabledHttpsPageHtml = """
    <!DOCTYPE html>
    <html lang="zh-CN">
    <head>
        <meta charset="utf-8" />
        <title>HTTPS 已禁用</title>
    </head>
    <body style="font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;">
        <div style="text-align:center;">
            <h1>HTTPS 已禁用</h1>
            <p>请通过 HTTP 管理端口访问控制台，或在「证书与 HTTPS」中启用 HTTPS 后再访问本端口。</p>
        </div>
    </body>
    </html>
    """;

app.Use(async (context, next) =>
{
    if (context.Request.IsHttps && !configStore.Config.EnableHttps)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(DisabledHttpsPageHtml);
        return;
    }

    await next();
});

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

static bool IsPortAvailable(IPAddress address, int port)
{
    try
    {
        var listener = new TcpListener(address, port);
        if (address.Equals(IPAddress.IPv6Any))
        {
            listener.Server.DualMode = true;
        }

        listener.Start();
        listener.Stop();
        return true;
    }
    catch
    {
        return false;
    }
}

static async Task<byte[]> ReadAvailableAsync(ConnectionContext connection)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (true)
    {
        var result = await connection.Transport.Input.ReadAsync(timeout.Token);
        var buffer = result.Buffer;
        if (buffer.IsEmpty)
        {
            if (result.IsCompleted)
            {
                return Array.Empty<byte>();
            }

            connection.Transport.Input.AdvanceTo(buffer.Start, buffer.End);
            continue;
        }

        var data = buffer.ToArray();
        connection.Transport.Input.AdvanceTo(buffer.End);
        return data;
    }
}

static async Task FeedBackAndRunAsync(ConnectionContext connection, byte[] prefix, Func<Task> next)
{
    var originalInput = connection.Transport.Input;
    var originalOutput = connection.Transport.Output;

    // 将已读出的字节与后续数据回灌到一个新管道，供 UseHttps 读取。
    var upstream = new Pipe();
    _ = Task.Run(async () =>
    {
        try
        {
            await upstream.Writer.WriteAsync(prefix);
            await originalInput.CopyToAsync(upstream.Writer);
        }
        catch
        {
        }
        finally
        {
            await upstream.Writer.CompleteAsync();
        }
    });

    connection.Transport = new BufferedDuplexPipe(upstream.Reader, originalOutput);

    try
    {
        await next();
    }
    finally
    {
        connection.Transport = new BufferedDuplexPipe(originalInput, originalOutput);
    }
}

static async Task WritePlainHttpHintAsync(ConnectionContext connection)
{
    const string body = "此端口为 HTTPS 专用，请使用 https:// 协议访问（例如 https://127.0.0.1:6002）。";
    var payload = Encoding.UTF8.GetBytes(
        "HTTP/1.1 400 Bad Request\r\n" +
        "Content-Type: text/plain; charset=utf-8\r\n" +
        $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
        "Connection: close\r\n\r\n" +
        body);

    try
    {
        await connection.Transport.Output.WriteAsync(payload);
        await connection.Transport.Output.FlushAsync();
    }
    catch
    {
    }
}

sealed class BufferedDuplexPipe : IDuplexPipe
{
    public PipeReader Input { get; }

    public PipeWriter Output { get; }

    public BufferedDuplexPipe(PipeReader input, PipeWriter output)
    {
        Input = input;
        Output = output;
    }
}
