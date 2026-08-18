namespace Seeing.Pxy.Shared;

public static class TunnelHubMethods
{
    public const string RegisterClient = nameof(RegisterClient);

    public const string UpdateRules = nameof(UpdateRules);

    public const string NewConnection = nameof(NewConnection);

    public const string SendData = nameof(SendData);

    public const string CloseStream = nameof(CloseStream);
}

public class RegisterResult
{
    public bool Success { get; set; }

    public string? Message { get; set; }

    public List<RuleError>? RuleErrors { get; set; }
}

public class RuleError
{
    public string RuleId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public static class TunnelDefaults
{
    public const int DataFrameSize = 64 * 1024;

    public const int PerStreamBufferLimit = 1024 * 1024;
}
