using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR.Client;
using Seeing.Pxy.Client.Config;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Client.Tunnel;

public sealed class TunnelClientService : BackgroundService
{
    public enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
    }

    private readonly ClientConfigStore _configStore;
    private readonly ILogger<TunnelClientService> _logger;

    private readonly ConcurrentDictionary<string, LocalStreamSession> _streams = new();
    private HubConnection? _connection;
    private int _status;
    private string _lastError = string.Empty;

    public TunnelClientService(ClientConfigStore configStore, ILogger<TunnelClientService> logger)
    {
        _configStore = configStore;
        _logger = logger;
    }

    public ConnectionStatus Status => (ConnectionStatus)Volatile.Read(ref _status);

    public string LastError => _lastError;

    public int ActiveStreams => _streams.Count;

    public HubConnection? Connection => _connection;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("重连循环异常：{Message}", ex.Message);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            _logger.LogInformation("连接未建立，将在 5 秒后重试");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task ReconnectAsync()
    {
        _connection?.StopAsync().ContinueWith(_ => { });
    }

    private async Task ConnectAndRunAsync(CancellationToken stoppingToken)
    {
        var config = _configStore.Config;
        if (string.IsNullOrWhiteSpace(config.ServerUrl) || string.IsNullOrWhiteSpace(config.Token))
        {
            _logger.LogWarning("客户端配置不完整，等待配置");
            return;
        }

        var connection = new HubConnectionBuilder()
            .WithUrl($"{config.ServerUrl.TrimEnd('/')}/tunnel")
            .WithAutomaticReconnect()
            .Build();

        _connection = connection;

        connection.On<string, string, int>(TunnelHubMethods.NewConnection, OnNewConnection);
        connection.On<string, byte[]>(TunnelHubMethods.SendData, OnSendData);
        connection.On<string>(TunnelHubMethods.CloseStream, OnCloseStream);

        connection.Reconnecting += ex =>
        {
            _logger.LogWarning("连接中断，正在自动重连：{Message}", ex?.Message);
            return Task.CompletedTask;
        };

        connection.Reconnected += async connectionId =>
        {
            _logger.LogInformation("已重新连接到服务端，连接 ID {ConnectionId}", connectionId);
            if (!await RegisterAsync(connection, config).ConfigureAwait(false))
            {
                await connection.StopAsync().ConfigureAwait(false);
            }
        };

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += ex =>
        {
            Volatile.Write(ref _status, (int)ConnectionStatus.Disconnected);
            _connection = null;
            _logger.LogWarning("与服务端连接已断开：{Message}", ex?.Message);
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        _logger.LogInformation("正在连接服务端 {Server}", config.ServerUrl);
        Volatile.Write(ref _status, (int)ConnectionStatus.Connecting);
        try
        {
            await connection.StartAsync(stoppingToken).ConfigureAwait(false);
            if (!await RegisterAsync(connection, config).ConfigureAwait(false))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return;
            }

            Volatile.Write(ref _status, (int)ConnectionStatus.Connected);
            _logger.LogInformation("已连接服务端 {Server}", config.ServerUrl);

            await closed.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning("连接服务端失败：{Message}", ex.Message);
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }
        finally
        {
            Volatile.Write(ref _status, (int)ConnectionStatus.Disconnected);
        }
    }

    private async Task<bool> RegisterAsync(HubConnection connection, ClientConfig config)
    {
        try
        {
            var result = await connection.InvokeAsync<RegisterResult>(
                TunnelHubMethods.RegisterClient,
                config.Token,
                config.ClientName,
                config.Rules).ConfigureAwait(false);

            if (!result.Success)
            {
                _lastError = result.Message ?? string.Join("; ", result.RuleErrors?.Select(e => e.Message) ?? Array.Empty<string>());
                _logger.LogWarning("注册失败：{Error}", _lastError);
                return false;
            }

            if (result.RuleErrors is { Count: > 0 })
            {
                _lastError = string.Join("; ", result.RuleErrors.Select(e => e.Message));
                _logger.LogWarning("部分规则注册失败：{Error}", _lastError);
            }
            else
            {
                _lastError = string.Empty;
            }

            return true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning("注册请求异常：{Message}", ex.Message);
            return false;
        }
    }

    private async Task OnNewConnection(string streamId, string localHost, int localPort)
    {
        var connection = _connection;
        if (connection is null)
        {
            return;
        }

        TcpClient client;
        try
        {
            client = new TcpClient();
            await client.ConnectAsync(localHost, localPort).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("拨号 {Host}:{Port} 失败：{Message}", localHost, localPort, ex.Message);
            try
            {
                await connection.InvokeAsync(TunnelHubMethods.CloseStream, streamId).ConfigureAwait(false);
            }
            catch
            {
            }

            return;
        }

        var session = new LocalStreamSession
        {
            StreamId = streamId,
            TcpClient = client,
            Stream = client.GetStream(),
        };

        _streams[streamId] = session;
        _logger.LogInformation("建立本地转发 {StreamId} -> {Host}:{Port}，当前活动转发 {Count}", streamId, localHost, localPort, _streams.Count);
        _ = Task.Run(() => PumpLocalAsync(session, connection));
        _ = Task.Run(() => PumpToServerAsync(session, connection));
    }

    private Task OnSendData(string streamId, byte[] data)
    {
        if (_streams.TryGetValue(streamId, out var session))
        {
            try
            {
                return session.Stream.WriteAsync(data, session.Cts.Token).AsTask();
            }
            catch (Exception)
            {
                return CloseLocalAsync(streamId, notify: true);
            }
        }

        return Task.CompletedTask;
    }

    private Task OnCloseStream(string streamId)
        => CloseLocalAsync(streamId, notify: false);

    private async Task PumpLocalAsync(LocalStreamSession session, HubConnection connection)
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
        }
    }

    private async Task PumpToServerAsync(LocalStreamSession session, HubConnection connection)
    {
        try
        {
            await foreach (var data in session.Buffer.Reader.ReadAllAsync(session.Cts.Token).ConfigureAwait(false))
            {
                await connection.InvokeAsync(TunnelHubMethods.SendData, session.StreamId, data).ConfigureAwait(false);
            }

            await CloseLocalAsync(session.StreamId, notify: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            await CloseLocalAsync(session.StreamId, notify: true).ConfigureAwait(false);
        }
    }

    private async Task CloseLocalAsync(string streamId, bool notify)
    {
        if (!_streams.TryRemove(streamId, out var session))
        {
            return;
        }

        session.Cts.Cancel();
        session.Buffer.Writer.TryComplete();
        session.TcpClient.Dispose();
        _logger.LogInformation("本地转发 {StreamId} 已关闭，剩余活动转发 {Count}", streamId, _streams.Count);

        if (notify && _connection is not null)
        {
            try
            {
                await _connection.InvokeAsync(TunnelHubMethods.CloseStream, streamId).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
