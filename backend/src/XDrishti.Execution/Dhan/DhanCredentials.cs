using System.ComponentModel.DataAnnotations;

namespace XDrishti.Execution.Dhan;

/// <summary>Client id and access token. <see cref="ToString"/> is redacted so that a log line or an exception cannot print the token.</summary>
public sealed record DhanCredentials(string ClientId, string Token)
{
    public override string ToString() => "DhanCredentials(redacted)";
}

public interface IExecCredentials
{
    /// <summary>Null when the credentials are missing: the caller refuses locally, no request is made.</summary>
    DhanCredentials? Current();
}

/// <summary>Bound from the secret files <c>Execution__Sandbox__ClientId</c> and <c>Execution__Sandbox__Token</c> (created by the owner; this project never creates or stores them).</summary>
public sealed class DhanSandboxOptions
{
    public const string Section = "Execution:Sandbox";

    [Required(AllowEmptyStrings = true)]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = true)]
    public string Token { get; set; } = string.Empty;
}

internal sealed class SandboxCredentials(Microsoft.Extensions.Options.IOptions<DhanSandboxOptions> options) : IExecCredentials
{
    public DhanCredentials? Current()
    {
        var o = options.Value;
        return string.IsNullOrWhiteSpace(o.ClientId) || string.IsNullOrWhiteSpace(o.Token) ? null : new DhanCredentials(o.ClientId.Trim(), o.Token.Trim());
    }
}
