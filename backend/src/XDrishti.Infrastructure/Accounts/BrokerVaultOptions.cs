using System.ComponentModel.DataAnnotations;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>
/// The broker vault key (configuration <c>BrokerVault:Key</c>, secret file <c>BrokerVault__Key</c> from the Keychain item
/// <c>xdrishti/broker/vault-key</c>). Only key material: the encryption key is derived from it (<see cref="BrokerSecretCipher"/>).
/// </summary>
internal sealed class BrokerVaultOptions
{
    public const string SectionName = "BrokerVault";

    public const int MinKeyLength = 32;

    [Required]
    [MinLength(MinKeyLength)]
    public string Key { get; set; } = string.Empty;
}
