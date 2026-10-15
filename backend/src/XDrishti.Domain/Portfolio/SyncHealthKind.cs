namespace XDrishti.Domain.Portfolio;

/// <summary>
/// What a health counter is kept for (P2-16, ADR 0027): the holdings refresh, the trade read, the ledger read of an account, and the quote poll (kept against the primary Data account). Stored by name (lower case) in
/// <c>portfolio.broker_sync_state</c> - never rename a value. Only <see cref="Trades"/> and <see cref="Ledger"/> have a watermark and runs in <c>broker_sync_runs</c>.
/// </summary>
public enum SyncHealthKind
{
    Holdings,
    Trades,
    Ledger,
    Quotes,
}
