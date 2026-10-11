namespace XDrishti.Application.Auth;

/// <summary>Where a request came from, for the audit trail. Both values come from the outside and are untrusted.</summary>
public sealed record ClientInfo(string? Ip, string? UserAgent)
{
    /// <summary>For actions that do not come from a request (provisioning at deployment).</summary>
    public static readonly ClientInfo Deployment = new("deployment", null);
}
