using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using Seeing.Pxy.Server.Config;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Server.Tunnel;

public sealed class TunnelService
{
    private readonly ILogger<TunnelService> _logger;
    private readonly IHubContext<TunnelHub, ITunnelClient> _hub;
    private readonly ServerConfigStore _configStore;
    private readonly TcpPortManager _ports;

    private readonly ConcurrentDictionary<string, ClientSession> _clients = new();
    private readonly ConcurrentDictionary<string, StreamSession> _streams = new();
    private readonly ConcurrentDictionary<string, StreamStats> _stats = new();

    public TunnelService(
        ILogger<TunnelService> logger,
        IHubContext<TunnelHub, ITunnelClient> hub,
        ServerConfigStore configStore,
        TcpPortManager ports)
    {
        _logger = logger;
        _hub = hub;
        _configStore = configStore;
        _ports = ports;
    }

    public async Task<RegisterResult> RegisterAsync(string connectionId, string token, string clientName, List<ForwardRule> rules)
    {
        if (!_configStore.Config.Tokens.Contains(token))
        {
            return new RegisterResult { Success = false, Message = "token 校验失败" };
        }

        if (string.IsNullOrWhiteSpace(clientName))
        {
            return new RegisterResult { Success = false, Message = "客户端名称不能为空" };
        }

        if (_clients.TryGetValue(clientName, out var existing))
        {
            if (existing.ConnectionId == connectionId)
            {
                var existingErrors = await ApplyRulesAsync(existing, rules).ConfigureAwait(false);
                return existingErrors.Count > 0
                    ? new RegisterResult { Success = false, RuleErrors = existingErrors }
                    : new RegisterResult { Success = true };
            }

            _clients.TryRemove(clientName, out _);
            await _ports.UnbindClientAsync(clientName).ConfigureAwait(false);
            foreach (var stream in _streams.Values.Where(s => s.ClientName == clientName).ToList())
            {
                if (_streams.TryRemove(stream.StreamId, out _))
                {
                    stream.Cts.Cancel();
                    stream.Buffer.Writer.TryComplete();
                    stream.Socket.Dispose();
                }
            }

            _logger.LogInformation("客户端 {Client} 重连，替换旧连接", clientName);
        }

        var session = new ClientSession
        {
            ClientName = clientName,
            ConnectionId = connectionId,
        };

        if (!_clients.TryAdd(clientName, session))
        {
            return new RegisterResult { Success = false, Message = $"客户端名称 {clientName} 已被占用" };
        }

        var errors = await ApplyRulesAsync(session, rules).ConfigureAwait(false);
        if (session.Rules.Count == 0)
        {
            return new RegisterResult { Success = false, RuleErrors = errors };
        }

        _logger.LogInformation("客户端 {Client} 注册成功，规则 {Count} 条", clientName, session.Rules.Count);
        return new RegisterResult { Success = true, RuleErrors = errors.Count > 0 ? errors : null };
    }

    public async Task<RegisterResult> UpdateRulesAsync(string connectionId, string token, string clientName, List<ForwardRule> rules)
    {
        if (!_configStore.Config.Tokens.Contains(token))
        {
            return new RegisterResult { Success = false, Message = "token 校验失败" };
        }

        if (!_clients.TryGetValue(clientName, out var session))
        {
            return new RegisterResult { Success = false, Message = "客户端未注册" };
        }

        if (session.ConnectionId != connectionId)
        {
            return new RegisterResult { Success = false, Message = "连接标识不匹配" };
        }

        var errors = await ApplyRulesAsync(session, rules).ConfigureAwait(false);
        return session.Rules.Count > 0
            ? new RegisterResult { Success = true, RuleErrors = errors.Count > 0 ? errors : null }
            : new RegisterResult { Success = false, RuleErrors = errors };
    }

    public async Task SendDataAsync(string streamId, byte[] data)
    {
        if (!_streams.TryGetValue(streamId, out var session))
        {
            return;
        }

        try
        {
            await session.Stream.WriteAsync(data, session.Cts.Token).ConfigureAwait(false);
            AddStats(session, 0, data.Length, 0);
        }
        catch (Exception)
        {
            await CloseStreamAsync(streamId, notifyClient: false).ConfigureAwait(false);
        }
    }

    public async Task CloseStreamAsync(string streamId, bool notifyClient = false)
    {
        if (!_streams.TryRemove(streamId, out var session))
        {
            return;
        }

        session.Cts.Cancel();
        session.Buffer.Writer.TryComplete();
        session.Socket.Dispose();

        var stats = GetStats(session.ClientName, session.RuleId);
        _logger.LogInformation(
            "转发 {StreamId} 关闭（客户端 {Client}），入站 {Inbound} 字节 / 出站 {Outbound} 字节，规则累计连接数 {Connections}",
            streamId,
            session.ClientName,
            stats.InboundBytes,
            stats.OutboundBytes,
            stats.ConnectionCount);

        if (notifyClient)
        {
            try
            {
                await _hub.Clients.Client(session.ConnectionId).CloseStream(streamId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "通知客户端关闭流 {StreamId} 失败", streamId);
            }
        }
    }

    public async Task DisconnectAsync(string connectionId)
    {
        var client = _clients.Values.FirstOrDefault(c => c.ConnectionId == connectionId);
        if (client is null)
        {
            return;
        }

        _clients.TryRemove(client.ClientName, out _);
        await _ports.UnbindClientAsync(client.ClientName).ConfigureAwait(false);

        foreach (var stream in _streams.Values.Where(s => s.ClientName == client.ClientName).ToList())
        {
            if (_streams.TryRemove(stream.StreamId, out _))
            {
                stream.Cts.Cancel();
                stream.Buffer.Writer.TryComplete();
                stream.Socket.Dispose();
            }
        }

        _logger.LogInformation("客户端 {Client} 已断开，资源已释放", client.ClientName);
    }

    public IReadOnlyList<ClientSession> GetClients() => _clients.Values.ToList();

    public IReadOnlyList<RuleStatus> GetRuleStatuses()
    {
        var result = new List<RuleStatus>();
        foreach (var client in _clients.Values)
        {
            foreach (var rule in client.Rules.Values)
            {
                result.Add(new RuleStatus
                {
                    ClientName = client.ClientName,
                    Rule = rule,
                    Stats = GetStats(client.ClientName, rule.Id),
                });
            }
        }

        return result;
    }

    private async Task<List<RuleError>> ApplyRulesAsync(ClientSession session, List<ForwardRule> rules)
    {
        var errors = new List<RuleError>();
        var newRules = rules.Where(r => r.Enabled).ToList();
        var newIds = newRules.Select(r => r.Id).ToHashSet();

        foreach (var old in session.Rules.Values.Where(r => !newIds.Contains(r.Id)).ToList())
        {
            await _ports.UnbindRuleAsync(session.ClientName, old.Id).ConfigureAwait(false);
            session.Rules.TryRemove(old.Id, out _);
        }

        foreach (var rule in newRules)
        {
            var config = _configStore.Config;
            var validationError = RuleValidator.Validate(rule, config);
            if (validationError is not null)
            {
                errors.Add(new RuleError { RuleId = rule.Id, Message = validationError });
                continue;
            }

            var (ok, error) = await _ports.TryBindAsync(
                session.ClientName,
                rule,
                config.ListenHost,
                OnAcceptedAsync).ConfigureAwait(false);

            if (!ok)
            {
                errors.Add(new RuleError { RuleId = rule.Id, Message = error ?? "绑定失败" });
                continue;
            }

            session.Rules[rule.Id] = rule;
        }

        return errors;
    }

    private async Task OnAcceptedAsync(PortBinding binding, Socket client)
    {
        if (!_clients.TryGetValue(binding.ClientName, out var clientSession))
        {
            client.Dispose();
            return;
        }

        var streamId = $"{binding.ClientName}-{Guid.NewGuid():N}";
        var rule = clientSession.Rules.TryGetValue(binding.RuleId, out var r)
            ? r
            : new ForwardRule();

        var stream = new StreamSession
        {
            StreamId = streamId,
            ClientName = binding.ClientName,
            RuleId = binding.RuleId,
            LocalHost = rule.LocalHost,
            LocalPort = rule.LocalPort,
            ConnectionId = clientSession.ConnectionId,
            Socket = client,
            Stream = new NetworkStream(client, ownsSocket: true),
        };

        _streams[streamId] = stream;
        AddStats(stream, 0, 0, 1);

        _logger.LogInformation(
            "公网端口 {Port} 收到连接 {Remote}，创建转发 {StreamId} -> {LocalHost}:{LocalPort}（客户端 {Client}）",
            binding.Port,
            client.RemoteEndPoint,
            streamId,
            stream.LocalHost,
            stream.LocalPort,
            binding.ClientName);

        _ = Task.Run(() => PumpInboundAsync(stream));
        _ = Task.Run(() => PumpToClientAsync(stream));

        try
        {
            await _hub.Clients.Client(clientSession.ConnectionId).NewConnection(streamId, stream.LocalHost, stream.LocalPort).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "通知客户端新连接失败 {StreamId}", streamId);
            await CloseStreamAsync(streamId, notifyClient: false).ConfigureAwait(false);
        }
    }

    private async Task PumpInboundAsync(StreamSession session)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(TunnelDefaults.DataFrameSize);
        try
        {
            while (!session.Cts.IsCancellationRequested)
            {
                var read = await session.Stream.ReadAsync(buffer, session.Cts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var data = new byte[read];
                Array.Copy(buffer, data, read);
                AddStats(session, data.Length, 0, 0);
                await session.Buffer.Writer.WriteAsync(data, session.Cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            session.Buffer.Writer.TryComplete();
            await CloseStreamAsync(session.StreamId, notifyClient: true).ConfigureAwait(false);
        }
    }

    private async Task PumpToClientAsync(StreamSession session)
    {
        try
        {
            await foreach (var data in session.Buffer.Reader.ReadAllAsync(session.Cts.Token).ConfigureAwait(false))
            {
                await _hub.Clients.Client(session.ConnectionId).SendData(session.StreamId, data).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            await CloseStreamAsync(session.StreamId, notifyClient: false).ConfigureAwait(false);
        }
    }

    private StreamStats GetStats(string clientName, string ruleId)
    {
        return _stats.GetOrAdd(Key(clientName, ruleId), _ => new StreamStats());
    }

    private void AddStats(StreamSession session, long inbound, long outbound, long connections)
    {
        var stats = GetStats(session.ClientName, session.RuleId);
        lock (stats)
        {
            stats.InboundBytes += inbound;
            stats.OutboundBytes += outbound;
            stats.ConnectionCount += connections;
        }
    }

    private static string Key(string clientName, string ruleId) => $"{clientName}:{ruleId}";
}
