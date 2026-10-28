using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LiveTrading;

/// <summary>A page of live entry executions in a range of sessions (size 1 to 200, range at most 92 days).</summary>
public sealed record LiveExecutionsQuery(Guid? AccountId, DateOnly? From, DateOnly? To, int Page, int Size) : IQuery<Result<LiveExecutionsPage>>;

/// <summary>The stored slippage estimates of a week (the newest week when none is given) as a snapshot.</summary>
public sealed record LiveSlippageQuery(DateOnly? WeekStart) : IQuery<Result<LiveSlippageView>>;

/// <summary>The stored capacity scores of a week (the newest week when none is given).</summary>
public sealed record LiveCapacityQuery(DateOnly? WeekStart) : IQuery<Result<LiveCapacityView>>;

/// <summary>The waterfall of one round trip, by the id of its execution.</summary>
public sealed record LiveWaterfallQuery(long EntryId) : IQuery<Result<LiveWaterfallView>>;

/// <summary>What stands behind a figure of the execution feedback: the basis, how many entries, over which sessions, against which references, with which bucket edges, and the stated limits. No figure is given without its n.</summary>
public sealed record LiveExecutionEvidenceView(string Basis, int N, DateOnly? FirstDate, DateOnly? LastDate, IReadOnlyList<string> ReferenceKinds, IReadOnlyList<decimal> BucketEdgesCr, IReadOnlyList<string> Limits);

/// <summary>
/// One live entry against its reference. Positive slippage is worse for the owner. Every figure is a state and a number only for a value (no reference, no order evidence or no stop gives <c>unavailable</c>, never 0); delay plus execution equals the total.
/// <see cref="WaterfallAvailable"/> says whether the round trip closed in the session with a graded plan, so the waterfall can be read.
/// </summary>
public sealed record LiveExecutionView(
    long Id,
    Guid AccountId,
    long ItemId,
    DateOnly Date,
    string Symbol,
    string Side,
    string Style,
    string Product,
    string Bucket,
    string ReferenceKind,
    long FillQty,
    decimal FillVwap,
    decimal? ReferencePrice,
    decimal? ArrivalPrice,
    decimal? ParticipationPct,
    int? EntryDelaySeconds,
    MetricView TotalRupees,
    MetricView DelayRupees,
    MetricView ExecutionRupees,
    MetricView TotalTicks,
    MetricView TotalBps,
    MetricView TotalR,
    MetricView DelayR,
    MetricView ExecutionR,
    bool WaterfallAvailable);

/// <summary>A page of executions with the evidence.</summary>
public sealed record LiveExecutionsPage(IReadOnlyList<LiveExecutionView> Items, int Page, int Size, int Total, LiveExecutionEvidenceView Evidence);

/// <summary>One slippage row: a bucket, side, style and reference kind with its n; the mean and median (in basis points) with their intervals are states, a number only for a value. <see cref="Proposal"/> is a sentence nothing reads automatically.</summary>
public sealed record LiveSlippageRowView(
    string Bucket,
    string Side,
    string Style,
    string ReferenceKind,
    int N,
    MetricView Mean,
    MetricView MeanLo,
    MetricView MeanHi,
    MetricView Median,
    MetricView MedianLo,
    MetricView MedianHi,
    MetricView MeanTicks,
    MetricView MeanR,
    string State,
    string? Proposal);

/// <summary>The slippage snapshot of one week. <see cref="State"/> is <c>empty</c> when nothing is stored for the week.</summary>
public sealed record LiveSlippageView(DateOnly? WeekStart, string State, IReadOnlyList<LiveSlippageRowView> Rows, LiveExecutionEvidenceView Evidence);

/// <summary>One capacity row: the share of orders filled in full for a scope (the account, or a bucket) with the counts by class.</summary>
public sealed record LiveCapacityRowView(string Scope, string Key, int NOrders, int NFull, int NPartial, int NRejected, int NQueueMiss, int NPriceMiss, int NUnclassified, MetricView Score, MetricView Lo, MetricView Hi, string State);

/// <summary>The capacity snapshot of one week.</summary>
public sealed record LiveCapacityView(DateOnly? WeekStart, string State, IReadOnlyList<LiveCapacityRowView> Rows, LiveExecutionEvidenceView Evidence);

/// <summary>One line of the waterfall: <see cref="Op"/> is start, minus or total; <see cref="R"/> is unavailable when the plan has no stop (the rupees are then the result).</summary>
public sealed record LiveWaterfallRowView(string Label, string Op, MetricView R, decimal Rupees);

/// <summary>
/// The waterfall of one round trip (plan result, entry slippage, exit difference, actual gross, charges, actual net). <see cref="State"/> is <c>value</c> or <c>unavailable</c> (the position did not close in the session, or the plan was not graded); then there are no rows
/// and <see cref="Notes"/> says why.
/// </summary>
public sealed record LiveWaterfallView(long EntryId, string Symbol, DateOnly Date, string State, string Basis, bool Balanced, IReadOnlyList<LiveWaterfallRowView> Rows, IReadOnlyList<string> Notes);
