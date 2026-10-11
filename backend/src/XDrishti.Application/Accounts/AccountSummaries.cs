using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Accounts;

/// <summary>Short, secret-free texts for the audit trail.</summary>
internal static class AccountSummaries
{
    /// <summary>For example <c>data primary, trading on, portfolio off</c>.</summary>
    public static string Roles(BrokerAccount account) =>
        $"data {Data(account)}, trading {OnOff(account.Trading.Enabled)}, portfolio {OnOff(account.Portfolio.Enabled)}";

    public static string Data(BrokerAccount account) =>
        !account.DataEnabled ? "off" : account.DataPrimary ? "primary" : account.DataStandby ? "standby" : "on";

    public static string OnOff(bool on) => on ? "on" : "off";

    /// <summary>For example <c>API key and API secret set</c> — names only, never values.</summary>
    public static string SecretsSet(IEnumerable<BrokerSecretName> names) => Join(names) + " set";

    /// <summary>For example <c>API key replaced</c>.</summary>
    public static string SecretsReplaced(IEnumerable<BrokerSecretName> names) => Join(names) + " replaced";

    private static string Join(IEnumerable<BrokerSecretName> names) => string.Join(" and ", names.Select(AccountCodes.Label));
}
