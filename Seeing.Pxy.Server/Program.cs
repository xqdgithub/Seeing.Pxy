using Seeing.Pxy.Server.Components;
using Seeing.Pxy.Server.Config;
using Seeing.Pxy.Server.Tunnel;

var builder = WebApplication.CreateBuilder(args);

var configStore = new ServerConfigStore(builder.Environment);
builder.WebHost.UseUrls($"http://{configStore.Config.ListenHost}:{configStore.Config.ManagementPort}");

builder.Services.AddSingleton(configStore);
builder.Services.AddSingleton<TcpPortManager>();
builder.Services.AddSingleton<TunnelService>();

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
