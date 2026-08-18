using Seeing.Pxy.Server.Tunnel;
using Seeing.Pxy.Shared;

namespace Seeing.Pxy.Tests;

public class RuleValidatorTests
{
    private static readonly ServerConfig Config = new()
    {
        MinAllowedPort = 6100,
        MaxAllowedPort = 6200,
    };

    [Fact]
    public void Validate_Accepts_InRange_Port()
    {
        var rule = new ForwardRule { RemotePort = 6150, LocalHost = "127.0.0.1", LocalPort = 8080 };
        Assert.Null(RuleValidator.Validate(rule, Config));
    }

    [Fact]
    public void Validate_Rejects_Port_Below_Range()
    {
        var rule = new ForwardRule { RemotePort = 6099, LocalHost = "127.0.0.1", LocalPort = 8080 };
        Assert.NotNull(RuleValidator.Validate(rule, Config));
    }

    [Fact]
    public void Validate_Rejects_Port_Above_Range()
    {
        var rule = new ForwardRule { RemotePort = 6201, LocalHost = "127.0.0.1", LocalPort = 8080 };
        Assert.NotNull(RuleValidator.Validate(rule, Config));
    }

    [Fact]
    public void Validate_Rejects_Invalid_LocalPort()
    {
        var rule = new ForwardRule { RemotePort = 6150, LocalHost = "127.0.0.1", LocalPort = 0 };
        Assert.NotNull(RuleValidator.Validate(rule, Config));
    }

    [Fact]
    public void Validate_Rejects_Empty_LocalHost()
    {
        var rule = new ForwardRule { RemotePort = 6150, LocalHost = " ", LocalPort = 8080 };
        Assert.NotNull(RuleValidator.Validate(rule, Config));
    }
}
