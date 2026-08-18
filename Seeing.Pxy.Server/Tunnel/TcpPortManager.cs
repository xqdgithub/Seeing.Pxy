using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Server.Tunnel;

public sealed class TcpPortManager
{
    private readonly ILogger<TcpPortManager> _logger;
    private readonly ConcurrentDictionary<int, PortBinding> _bindings = new();

    public TcpPortManager(ILogger<TcpPortManager> logger)
    {
        _logger = logger;
    }

    public async Task<(bool Ok, string? Error)> TryBindAsync(
        string clientName,
        ForwardRule rule,
        string listenHost,
        Func<PortBinding, Socket, Task> onAccepted)
    {
        if (_bindings.TryGetValue(rule.RemotePort, out var existing))
        {
            return (false, $"端口 {rule.RemotePort} 已被 {existing.ClientName} 占用");
        }

        TcpListener? listener = null;
        try
        {
            listener = CreateListener(listenHost, rule.RemotePort);
            listener.Start();
        }
        catch (Exception ex)
        {
            listener?.Stop();
            return (false, $"监听端口 {rule.RemotePort} 失败：{ex.Message}");
        }

        var binding = new PortBinding
        {
            Port = rule.RemotePort,
            ClientName = clientName,
            RuleId = rule.Id,
            Listener = listener,
            Cts = new CancellationTokenSource(),
        };

        if (!_bindings.TryAdd(rule.RemotePort, binding))
        {
            listener.Stop();
            return (false, $"端口 {rule.RemotePort} 已被占用");
        }

        binding.AcceptLoop = AcceptLoopAsync(binding, onAccepted);
        _logger.LogInformation("客户端 {Client} 的端口 {Port} 已开始监听", clientName, rule.RemotePort);
        return (true, null);
    }

    public async Task UnbindClientAsync(string clientName)
    {
        foreach (var binding in _bindings.Values.Where(b => b.ClientName == clientName).ToList())
        {
            await UnbindAsync(binding);
        }
    }

    public async Task UnbindRuleAsync(string clientName, string ruleId)
    {
        var binding = _bindings.Values.FirstOrDefault(b => b.ClientName == clientName && b.RuleId == ruleId);
        if (binding is not null)
        {
            await UnbindAsync(binding);
        }
    }

    private async Task UnbindAsync(PortBinding binding)
    {
        if (_bindings.TryRemove(binding.Port, out _))
        {
            binding.Cts.Cancel();
            binding.Listener.Stop();
            try
            {
                await binding.AcceptLoop.ConfigureAwait(false);
            }
            catch
            {
            }

            _logger.LogInformation("客户端 {Client} 的端口 {Port} 已停止监听", binding.ClientName, binding.Port);
        }
    }

    private async Task AcceptLoopAsync(PortBinding binding, Func<PortBinding, Socket, Task> onAccepted)
    {
        while (!binding.Cts.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await binding.Listener.AcceptSocketAsync(binding.Cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "端口 {Port} 接受连接失败", binding.Port);
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await onAccepted(binding, client).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "处理端口 {Port} 的连接失败", binding.Port);
                    client.Dispose();
                }
            });
        }
    }

    private static TcpListener CreateListener(string listenHost, int port)
    {
        if (string.IsNullOrWhiteSpace(listenHost) || listenHost is "0.0.0.0" or "*")
        {
            var dualStack = new TcpListener(IPAddress.IPv6Any, port);
            dualStack.Server.DualMode = true;
            return dualStack;
        }

        return new TcpListener(IPAddress.Parse(listenHost), port);
    }
}

public sealed class PortBinding
{
    public int Port { get; init; }

    public string ClientName { get; init; } = string.Empty;

    public string RuleId { get; init; } = string.Empty;

    public TcpListener Listener { get; init; } = null!;

    public CancellationTokenSource Cts { get; init; } = null!;

    public Task AcceptLoop { get; set; } = Task.CompletedTask;
}
