using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Attention;

/// <summary>Where the attention items get their facts from (the ports, in production; made-up facts in tests).</summary>
internal interface IAttentionFactsSource
{
    ValueTask<AttentionFacts> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the facts the attention items are derived from (ADR 0027), set-based: one read per kind of fact for all accounts together, never one per account - the accounts, their Dhan logins, the health figures, the latest succeeded trades and ledger run of each
/// Portfolio account, the flags of the holdings those runs name, the pending ledger dividends and the unmatched securities. A fixed number of reads however many accounts there are.
/// </summary>
internal sealed class AttentionFactsLoader(
    IAccountStore accounts,
    IBrokerConnectionStore connections,
    ISyncHealthStore health,
    IBrokerSyncStore runs,
    IAttentionQueries queries,
    IDataAccountStatus dataAccount,
    TimeProvider clock) : IAttentionFactsSource
{
    /// <summary>A traded security with no match in the instrument master counts for this many days after the trade.</summary>
    public const int TradedWithinDays = 90;

    public async ValueTask<AttentionFacts> LoadAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var active = await accounts.ListActiveAsync(cancellationToken);
        var ids = active.Select(a => a.Id).ToList();
        var logins = await connections.ListAsync(ids, cancellationToken);
        var data = await dataAccount.GetAsync(cancellationToken);

        var rows = await health.ListAsync(data.AccountId is { } dataId ? [.. ids, dataId] : ids, cancellationToken);
        var portfolio = active.Where(a => a.Portfolio.Enabled).Select(a => a.Id).ToList();
        IReadOnlyDictionary<Guid, BrokerSyncRun> latest = portfolio.Count == 0 ? new Dictionary<Guid, BrokerSyncRun>() : await runs.ListLastSucceededAsync(portfolio, BrokerSyncKind.Trades, cancellationToken);
        var counts = latest.ToDictionary(r => r.Key, r => r.Value.Counts);
        IReadOnlyDictionary<Guid, BrokerSyncRun> latestLedger = portfolio.Count == 0 ? new Dictionary<Guid, BrokerSyncRun>() : await runs.ListLastSucceededAsync(portfolio, BrokerSyncKind.Ledger, cancellationToken);
        var named = counts.Values.SelectMany(c => c.NotRebuilt).Where(n => n.HoldingId is not null).Select(n => n.HoldingId!.Value).Distinct().ToList();

        IReadOnlyDictionary<Guid, HoldingFlags> flags = named.Count == 0 ? new Dictionary<Guid, HoldingFlags>() : await queries.HoldingFlagsAsync(named, cancellationToken);
        IReadOnlyDictionary<Guid, PendingDividends> dividends = portfolio.Count == 0 ? new Dictionary<Guid, PendingDividends>() : await queries.PendingDividendsAsync(portfolio, cancellationToken);
        IReadOnlyDictionary<Guid, UnmatchedCounts> unmatched = portfolio.Count == 0 ? new Dictionary<Guid, UnmatchedCounts>() : await queries.UnmatchedAsync(portfolio, XDrishti.Domain.Market.IndiaTime.Today(now).AddDays(-TradedWithinDays), cancellationToken);
        return new AttentionFacts(
            now,
            [.. active.Select(a => Account(a, logins.GetValueOrDefault(a.Id), now))],
            rows.ToDictionary(h => (h.AccountId, h.Kind)),
            counts,
            latestLedger.ToDictionary(r => r.Key, r => r.Value.Counts),
            flags,
            dividends,
            unmatched,
            data.AccountId,
            data.Name);
    }

    private static AttentionAccount Account(BrokerAccount account, BrokerConnection? login, DateTimeOffset now) =>
        new(account.Id, account.Name, account.Portfolio.Enabled, login?.StatusAt(now) ?? ConnectionStatus.NotConnected, login?.ExpiresAt, login?.LastConnectedAt);
}
