using System.ComponentModel.DataAnnotations;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>
/// The key of the execution token hand-over (ADR 0171; configuration <c>ExecHandover:Key</c>, secret file <c>ExecHandover__Key</c> from the Keychain item
/// <c>xdrishti/exec/handover-key</c>). A second key, apart from the vault key: it opens the day's access tokens only, never the long-lived secrets. Given to
/// <c>xd-api</c> and <c>xd-exec</c> only.
/// </summary>
internal sealed class ExecHandoverOptions
{
    public const string SectionName = "ExecHandover";

    [Required]
    [MinLength(SecretCipher.MinKeyLength)]
    public string Key { get; set; } = string.Empty;
}
