# Seeing.Pxy

类似 frp 的内网穿透工具，基于 .NET 10 + SignalR + Blazor Server（Ant Design Blazor）。

服务端部署在公网服务器，客户端部署在内网机器并主动连接服务端。外部访问服务端指定公网端口时，流量经 SignalR 长连接转发到客户端指定的本地地址与端口。

## 项目结构

| 项目 | 说明 |
|------|------|
| `Seeing.Pxy.Shared` | 共享库：配置模型、规则 DTO、消息协议、流量统计模型 |
| `Seeing.Pxy.Server` | 服务端：SignalR Hub + 公网 TCP 监听器 + Blazor Server 管理页 |
| `Seeing.Pxy.Client` | 客户端：SignalR 客户端 + 本地 TCP 转发器 + Blazor Server 管理页 |
| `Seeing.Pxy.Tests` | 单元测试 |

## 工作原理

1. 客户端启动后，通过 SignalR 连接服务端，携带 token、客户端名称与规则列表注册。
2. 服务端校验 token 后，按规则在公网端口上监听 TCP。
3. 外部连接访问公网端口时，服务端分配 `streamId` 并通知客户端。
4. 客户端拨号到 `LocalHost:LocalPort`，双向转发字节流（单条 SignalR 连接多路复用）。
5. 任一端关闭则通知对端清理。

## 快速开始

### 1. 服务端（公网服务器）

```bash
cd Seeing.Pxy.Server
dotnet run -c Release
```

首次运行会在 `ContentRootPath` 生成 `server.json`，也可直接创建：

```json
{
  "ListenHost": "0.0.0.0",
  "ManagementPort": 6001,
  "EnableHttps": true,
  "HttpsPort": 6002,
  "CertificatePath": "/etc/ssl/seeingyou.pfx",
  "CertificatePassword": "your-password",
  "Tokens": ["your-token"],
  "MinAllowedPort": 6100,
  "MaxAllowedPort": 6200
}
```

- 管理页：`http://服务器IP:6001` 或 `https://服务器IP:6002`（可编辑 token、查看客户端与流量统计）
- HTTPS 证书：配置 `CertificatePath`/`CertificatePassword` 指向 PFX 证书；未配置时尝试使用 .NET 开发证书，仍不可用则仅监听 HTTP
- 需在防火墙/安全组放行：管理端口（6001、6002）+ 所有映射的公网端口（TCP）

### 2. 客户端（内网机器）

```bash
cd Seeing.Pxy.Client
dotnet run -c Release
```

首次运行生成 `client.json`，编辑后重启或直接在管理页配置：

```json
{
  "ServerUrl": "https://公网服务器:6002",
  "Token": "your-token",
  "ClientName": "home-pc",
  "Rules": [
    {
      "RemotePort": 6100,
      "LocalHost": "127.0.0.1",
      "LocalPort": 22,
      "Enabled": true
    }
  ]
}
```

- 客户端管理页：`http://localhost:6001`（编辑规则、服务端地址、token，实时查看连接状态）
- `ServerUrl` 可用 `http://公网服务器:6001` 或 `https://公网服务器:6002`（需服务端已启用 HTTPS）

### 3. 验证

```
外部机器: telnet 公网服务器 6100   →  转发到内网 127.0.0.1:22
```

## 配置说明

- `Token`：预共享密钥，服务端 `Tokens` 列表可配多个。
- `ClientName`：客户端唯一名称，同一时刻同名客户端只能有一个在线。
- `RemotePort`：服务端公网端口，需在 `MinAllowedPort`/`MaxAllowedPort` 范围内且全局唯一。
- `LocalHost`/`LocalPort`：客户端可达的任意内网地址。

## 构建与测试

```bash
dotnet build Seeing.Pxy.slnx
dotnet test Seeing.Pxy.Tests
```

## 说明

- 当前仅支持 TCP 转发；UDP、HTTP(S) 域名路由不在范围内。
- 数据通道默认不加密，公网部署建议在反向代理层加 TLS。
