namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>
/// One live entry measured against what the plan promised (<c>trading.live_executions</c>, spec P11-02): one per entry outcome. Slippage figures follow the sign rule (positive is worse for the owner) and are null, never zero, when an input does not exist.
/// The exit columns (plan prices, exit VWAP and quantity, live charges) are what the waterfall of the round trip is built from.
/// </summary>
public sealed record NewLiveExecution(
    long OutcomeId,
    Guid AccountId,
    long ItemId,
    DateOnly ForDate,
    string Symbol,
    string Side,
    string Style,
    string Product,
    string Bucket,
    decimal? AdvTurnoverCr,
    decimal? ParticipationPct,
    decimal? Tick,
    string ReferenceKind,
    decimal? ReferencePrice,
    decimal? RiskPerShare,
    decimal? ArrivalPrice,
    decimal FillVwap,
    long FillQty,
    double? SlipTotal,
    double? SlipDelay,
    double? SlipExec,
    double? SlipTotalTicks,
    double? SlipTotalBps,
    double? SlipTotalR,
    double? SlipDelayR,
    double? SlipExecR,
    int? EntryDelaySeconds,
    decimal? PlanEntry,
    decimal? PlanExit,
    decimal? PlanStop,
    double PlanChargesR,
    decimal? ExitVwap,
    long? ExitQty,
    decimal? LiveCharges);

/// <summary>A stored execution with its row id (the key of the waterfall endpoint).</summary>
public sealed record StoredLiveExecution(long Id, NewLiveExecution Row, DateTimeOffset ComputedAt);

/// <summary>A page of stored executions with the total that matched.</summary>
public sealed record StoredExecutionPage(IReadOnlyList<StoredLiveExecution> Items, int Total);

/// <summary>The class of one entry order for the capacity score (<c>trading.live_order_classes</c>, one row per account and broker order id, insert-only).</summary>
public sealed record LiveOrderClassRow(Guid AccountId, string BrokerOrderId, long OutcomeId, DateOnly ForDate, string Symbol, string Bucket, string OrderType, long Qty, long FilledQty, string Class);

/// <summary>A slippage estimate of one week and key (<c>trading.slippage_estimates</c>, insert-only, one snapshot per week and key).</summary>
public sealed record NewSlippageEstimate(
    DateOnly WeekStart,
    string Bucket,
    string Side,
    string Style,
    string ReferenceKind,
    int N,
    string State,
    string? Reason,
    double? MeanBps,
    double? MeanLo,
    double? MeanHi,
    double? MedianBps,
    double? MedianLo,
    double? MedianHi,
    double? MeanTicks,
    double? MeanR,
    string? Proposal,
    string InputHash);

/// <summary>A capacity score of one week and scope (<c>trading.capacity_scores</c>, insert-only).</summary>
public sealed record NewCapacityScore(
    DateOnly WeekStart,
    string Scope,
    string ScopeKey,
    int NOrders,
    int NFull,
    int NPartial,
    int NRejected,
    int NQueueMiss,
    int NPriceMiss,
    int NUnclassified,
    double? Score,
    double? Lo,
    double? Hi,
    string State,
    string? Reason,
    int PriorStrength,
    string InputHash);
