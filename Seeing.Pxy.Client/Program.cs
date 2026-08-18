using Seeing.Pxy.Client.Components;
using Seeing.Pxy.Client.Config;
using Seeing.Pxy.Client.Tunnel;

var builder = WebApplication.CreateBuilder(args);

var configStore = new ClientConfigStore(builder.Environment);

builder.Services.AddSingleton(configStore);
builder.Services.AddHostedService<TunnelClientService>();

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

app.Run();
