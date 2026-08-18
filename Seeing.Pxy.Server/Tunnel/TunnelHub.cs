using Microsoft.AspNetCore.SignalR;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Server.Tunnel;

public sealed class TunnelHub : Hub<ITunnelClient>
{
    private readonly TunnelService _service;

    public TunnelHub(TunnelService service)
    {
        _service = service;
    }

    public Task<RegisterResult> RegisterClient(string token, string clientName, List<ForwardRule> rules)
        => _service.RegisterAsync(Context.ConnectionId, token, clientName, rules);

    public Task<RegisterResult> UpdateRules(string token, string clientName, List<ForwardRule> rules)
        => _service.UpdateRulesAsync(Context.ConnectionId, token, clientName, rules);

    public Task SendData(string streamId, byte[] data)
        => _service.SendDataAsync(streamId, data);

    public Task CloseStream(string streamId)
        => _service.CloseStreamAsync(streamId, notifyClient: false);

    public override Task OnDisconnectedAsync(Exception? exception)
        => _service.DisconnectAsync(Context.ConnectionId);
}
