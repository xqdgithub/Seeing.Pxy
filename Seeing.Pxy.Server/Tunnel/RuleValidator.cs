using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Server.Tunnel;

public static class RuleValidator
{
    public static string? Validate(ForwardRule rule, ServerConfig config)
    {
        if (rule.RemotePort < config.MinAllowedPort || rule.RemotePort > config.MaxAllowedPort)
        {
            return $"公网端口 {rule.RemotePort} 不在允许范围 {config.MinAllowedPort}-{config.MaxAllowedPort} 内";
        }

        if (rule.RemotePort <= 0 || rule.RemotePort > 65535)
        {
            return $"公网端口 {rule.RemotePort} 无效";
        }

        if (rule.LocalPort <= 0 || rule.LocalPort > 65535)
        {
            return $"本地端口 {rule.LocalPort} 无效";
        }

        if (string.IsNullOrWhiteSpace(rule.LocalHost))
        {
            return "本地地址不能为空";
        }

        return null;
    }
}
