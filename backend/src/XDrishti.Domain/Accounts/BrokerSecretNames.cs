namespace XDrishti.Domain.Accounts;

/// <summary>The storage keys of <see cref="BrokerSecretName"/> (database column <c>name</c> and part of the encryption's associated data).</summary>
public static class BrokerSecretNames
{
    public const int MaxLength = 32;

    /// <summary>The secrets the owner enters for an account (write-only). The access token is obtained by connecting and stored separately.</summary>
    public static IReadOnlyList<BrokerSecretName> OwnerSecrets { get; } = [BrokerSecretName.ApiKey, BrokerSecretName.ApiSecret];

    public static string Key(BrokerSecretName name) => name switch
    {
        BrokerSecretName.ApiKey => "api_key",
        BrokerSecretName.ApiSecret => "api_secret",
        BrokerSecretName.AccessToken => "access_token",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    public static bool TryParseKey(string? key, out BrokerSecretName name)
    {
        foreach (var candidate in Enum.GetValues<BrokerSecretName>())
        {
            if (string.Equals(Key(candidate), key, StringComparison.Ordinal))
            {
                name = candidate;
                return true;
            }
        }

        name = default;
        return false;
    }
}
