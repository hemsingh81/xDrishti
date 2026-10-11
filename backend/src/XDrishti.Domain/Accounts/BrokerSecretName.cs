namespace XDrishti.Domain.Accounts;

/// <summary>
/// The only secrets the broker vault stores for an account. Each name has a fixed storage key (<see cref="BrokerSecretNames"/>) that is
/// also bound into the encryption, so never rename one.
/// </summary>
public enum BrokerSecretName
{
    /// <summary>Dhan <c>app_id</c>.</summary>
    ApiKey,

    /// <summary>Dhan <c>app_secret</c>.</summary>
    ApiSecret,

    /// <summary>The daily access token obtained by the consent login.</summary>
    AccessToken,
}
