using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Accounts;

/// <summary>Builds the response shapes from accounts (profile names, which secrets are set, connection). Shared by every handler that returns an account.</summary>
internal sealed class AccountViews(IRiskProfileStore riskProfiles, IBrokerSecretVault vault, IBrokerConnectionStore connections, TimeProvider clock)
{
    public async ValueTask<IReadOnlyList<AccountSummary>> SummariesAsync(IReadOnlyList<BrokerAccount> accounts, CancellationToken cancellationToken)
    {
        var names = await ProfileNamesAsync(cancellationToken);
        var ids = accounts.Select(a => a.Id).ToList();
        var secrets = await vault.ListAsync(ids, cancellationToken);
        var connected = await connections.ListAsync(ids, cancellationToken);
        var now = clock.GetUtcNow();
        var result = new List<AccountSummary>(accounts.Count);
        foreach (var account in accounts)
        {
            var connection = Connection(connected.GetValueOrDefault(account.Id), now);
            result.Add(new AccountSummary(
                account.Id,
                account.Slug,
                account.Name,
                AccountCodes.Name(account.Broker),
                account.MaskedClientId,
                account.Color,
                Roles(account, names),
                Secrets(account, secrets),
                account.ApiKeyExpiresOn,
                connection.Status,
                connection));
        }

        return result;
    }

    public async ValueTask<AccountDetail> DetailAsync(BrokerAccount account, CancellationToken cancellationToken)
    {
        var names = await ProfileNamesAsync(cancellationToken);
        var secrets = await vault.ListAsync([account.Id], cancellationToken);
        var connected = await connections.ListAsync([account.Id], cancellationToken);
        var connection = Connection(connected.GetValueOrDefault(account.Id), clock.GetUtcNow());
        var trading = account.Trading;
        var profileName = trading.RiskProfileId is { } id && names.TryGetValue(id, out var name) ? name : null;

        var styles = new List<string>(2);
        if (trading.Intraday)
        {
            styles.Add(AccountCodes.Intraday);
        }

        if (trading.Swing)
        {
            styles.Add(AccountCodes.Swing);
        }

        return new AccountDetail(
            account.Id,
            account.Slug,
            account.Name,
            AccountCodes.Name(account.Broker),
            account.MaskedClientId,
            account.Color,
            account.Notes,
            Secrets(account, secrets),
            account.ApiKeyIssuedOn,
            account.ApiKeyExpiresOn,
            connection.Status,
            connection,
            account.CreatedAt,
            account.UpdatedAt,
            Roles(account, names),
            new TradingSettingsView(
                trading.Enabled,
                AccountCodes.Name(trading.Mode),
                trading.RiskProfileId,
                profileName,
                AccountCodes.Name(trading.CapitalSource),
                trading.CapitalAmount,
                styles,
                new InstrumentsView(
                    AccountCodes.Names(trading.IntradayInstruments),
                    AccountCodes.Names(trading.SwingBuyInstruments),
                    AccountCodes.Names(trading.SwingSellInstruments)),
                new AccountOverrides(trading.OverrideRiskPerTradePct, trading.OverrideMaxNewEntriesPerDay, trading.OverrideMaxOpenPositions, trading.OverrideDailyLossPct)),
            new PortfolioSettingsView(account.Portfolio.Enabled, account.Portfolio.DefaultBucket, account.Portfolio.HistoryFrom, account.Portfolio.IncludeInTotals),
            new DataSettingsView(account.DataEnabled, account.DataPrimary, account.DataStandby, account.LiveFeedBudget));
    }

    /// <summary>The connection as shown: status derived at <paramref name="now"/> (a passed expiry reads as expired), the holder's id masked.</summary>
    public static ConnectionView Connection(BrokerConnection? connection, DateTimeOffset now) => connection is null
        ? new ConnectionView(AccountCodes.Name(ConnectionStatus.NotConnected), null, null, null, null, null)
        : new ConnectionView(
            AccountCodes.Name(connection.StatusAt(now)),
            connection.ExpiresAt,
            connection.BrokerUserName,
            connection.BrokerUserId is { } id ? BrokerAccount.MaskClientId(id) : null,
            connection.LastConnectedAt,
            connection.LastError);

    /// <summary>The owner-entered secrets (API key, API secret), each set or not set.</summary>
    private static List<SecretStatusView> Secrets(BrokerAccount account, IReadOnlyDictionary<Guid, IReadOnlyList<BrokerSecretInfo>> stored)
    {
        var set = stored.GetValueOrDefault(account.Id) ?? [];
        return [.. BrokerSecretNames.OwnerSecrets.Select(name =>
        {
            var info = set.FirstOrDefault(s => s.Name == name);
            return new SecretStatusView(AccountCodes.Field(name), info is not null, info?.UpdatedAt);
        })];
    }

    private static RolesView Roles(BrokerAccount account, IReadOnlyDictionary<Guid, string> profileNames)
    {
        var trading = account.Trading;
        var profile = trading.RiskProfileId is { } id && profileNames.TryGetValue(id, out var name) ? name : null;
        return new RolesView(
            new DataRoleView(account.DataEnabled, account.DataPrimary, account.DataStandby),
            new TradingRoleView(trading.Enabled, AccountCodes.Name(trading.Mode), profile),
            new PortfolioRoleView(account.Portfolio.Enabled));
    }

    private async ValueTask<IReadOnlyDictionary<Guid, string>> ProfileNamesAsync(CancellationToken cancellationToken) =>
        (await riskProfiles.ListAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Name);
}
