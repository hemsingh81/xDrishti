namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>
/// One ticket of one live account as the live-versus-paper report reads it (spec P11-03): its stored outcome, the plan grade's status and R, and the stored execution when it was entered. Plain stored facts; nothing here reaches a broker.
/// </summary>
public sealed record LiveTrip(long ItemId, Guid AccountId, DateOnly Date, string Symbol, string Side, string Strategy, string Outcome, string GradeStatus, double? GradeR, StoredLiveExecution? Execution);

/// <summary>The live side of the report: the tickets of a live account in a range of sessions, oldest first, one per stored outcome. Read only.</summary>
public interface ILivePairSource
{
    ValueTask<IReadOnlyList<LiveTrip>> TripsAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>One paper trade of the account and epoch: entry price and quantity, the exit price when closed, and the charges when a stored sheet priced them (rupees).</summary>
public sealed record PaperTrade(long ItemId, string Symbol, string Side, decimal EntryPrice, long Qty, decimal? ExitPrice, decimal? Charges);

/// <summary>The paper accounts of the current epoch. <see cref="Available"/> false means the paper record could not be read: the report says unavailable, never empty.</summary>
public sealed record PaperAccountsRead(bool Available, long? EpochId, DateOnly? StartedOn, IReadOnlyList<Guid> Accounts);

/// <summary>The paper trades of one account and epoch for some tickets, and the skip code of a ticket the paper account did not take.</summary>
public sealed record PaperTradesRead(bool Available, IReadOnlyList<PaperTrade> Trades, IReadOnlyDictionary<long, string> SkipCodes);

/// <summary>The paper side of the report (<c>paper.entries</c>, <c>paper.exits</c>, <c>paper.skips</c>) behind a port so the paper phase's final shape is isolated. Read only; the current epoch only.</summary>
public interface IPaperTradeSource
{
    ValueTask<PaperAccountsRead> AccountsAsync(CancellationToken cancellationToken);

    ValueTask<PaperTradesRead> TradesAsync(long epochId, Guid accountId, IReadOnlyList<long> itemIds, CancellationToken cancellationToken);
}

/// <summary>The settings of the report (<c>Live:Report</c>): thresholds, not switches. The report is read only and changes no behaviour.</summary>
public interface ILiveReportSettings
{
    /// <summary>The non-inferiority margin in R.</summary>
    double NonInferiorityR { get; }

    /// <summary>The fewest pairs for a verdict.</summary>
    int MinPairs { get; }

    /// <summary>The paper account to pair with when there are several; null when unset.</summary>
    Guid? PaperAccountId { get; }
}
