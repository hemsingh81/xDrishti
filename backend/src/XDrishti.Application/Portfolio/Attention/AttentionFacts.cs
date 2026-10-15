using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Attention;

/// <summary>One active account as the items see it: whether it has the Portfolio role on and the state of its Dhan login at the time of the read.</summary>
internal sealed record AttentionAccount(Guid Id, string Name, bool PortfolioOn, ConnectionStatus Status, DateTimeOffset? ExpiresAt, DateTimeOffset? LastConnectedAt);

/// <summary>
/// Everything the items are derived from, read set-based for all accounts at once (ADR 0027): the accounts with their logins, the health of every kind, the counts of each account's latest succeeded trades run (which holdings need review, which the trades do not explain),
/// the current flags of the holdings those name, the pending ledger dividends and the unmatched securities per account, and the primary Data account the quote poll is kept against.
/// </summary>
internal sealed record AttentionFacts(
    DateTimeOffset Now,
    IReadOnlyList<AttentionAccount> Accounts,
    IReadOnlyDictionary<(Guid Account, SyncHealthKind Kind), SyncHealth> Health,
    IReadOnlyDictionary<Guid, BrokerSyncCounts> TradeCounts,
    IReadOnlyDictionary<Guid, HoldingFlags> Flags,
    IReadOnlyDictionary<Guid, PendingDividends> Dividends,
    IReadOnlyDictionary<Guid, UnmatchedCounts> Unmatched,
    Guid? DataAccountId,
    string? DataAccountName);
