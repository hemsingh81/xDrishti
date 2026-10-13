using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>The statuses of the rebuild of a holding's lots (the <c>status</c> of the <c>rebuild</c> block of the API).</summary>
internal static class RebuildStatuses
{
    /// <summary>The trades explain the holding and the lots would change.</summary>
    public const string Ok = "ok";

    /// <summary>The trades do not fit the broker's quantity; nothing is applied.</summary>
    public const string QuantityMismatch = "quantity_mismatch";

    /// <summary>The account does not hold the security (any more); nothing to rebuild.</summary>
    public const string NoHolding = "no_holding";

    /// <summary>The lots are what the trades give already.</summary>
    public const string Unchanged = "unchanged";

    /// <summary>More than one holding of the account matches the security; nothing is applied.</summary>
    public const string Ambiguous = "ambiguous";
}

/// <summary>
/// What rebuilding one holding's lots from its trades would do. <see cref="Plan"/> is what <see cref="Holding.ReplaceLots"/> would make when <see cref="Status"/> is <c>ok</c> (empty otherwise). The replaced counts are of the lots that exist now
/// and would be removed: lots whose date the owner chose (<see cref="HandDatedLotsReplaced"/>) and lots a sync added (<see cref="SyncLotsReplaced"/>); both 0 unless the status is <c>ok</c>. <see cref="OpeningBefore"/> is the quantity the
/// current lots count as opening (imported lots still open or really sold), <see cref="OpeningAfter"/> what the trades leave as the broker's remainder. <see cref="OpenCostAfter"/> is what the open lots of the result cost
/// (also for <c>unchanged</c>), <see cref="BrokerCost"/> the broker's <c>Q × A</c> and <see cref="CostDelta"/> their difference: zero when the file has no sales and there is an opening lot to take the difference, otherwise the first-in first-out cost basis of the trades can differ from the
/// broker's average-cost figure. <see cref="CostNote"/> <c>opening_cost_fallback</c> says the opening lot costs the average cost because the trades cost more than it allows. All four are null unless the trades fit.
/// </summary>
internal sealed record HoldingRebuild(
    Holding? Holding,
    string Status,
    string? Reason,
    int LotsBefore,
    int LotsAfter,
    int HandDatedLotsReplaced,
    int SyncLotsReplaced,
    decimal? RealisedPnl,
    int AdjustedFor,
    long? Qty,
    long? Net,
    long? OpeningBefore,
    long? OpeningAfter,
    IReadOnlyList<LotPlan> Plan,
    string Digest,
    decimal? OpenCostAfter = null,
    decimal? BrokerCost = null,
    decimal? CostDelta = null,
    string? CostNote = null)
{
    public bool WillReplace => Status == RebuildStatuses.Ok;

    /// <summary>A security that has no single holding: <c>no_holding</c> or <c>ambiguous</c>.</summary>
    public static HoldingRebuild Without(string status, string key) =>
        new(null, status, null, 0, 0, 0, 0, null, 0, null, null, null, null, [], LotShapes.Digest([key, status]));
}
