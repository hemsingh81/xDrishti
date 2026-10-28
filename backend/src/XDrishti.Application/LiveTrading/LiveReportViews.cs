using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LiveTrading;

/// <summary>The live-versus-paper report of one live account (spec P11-03). <see cref="PaperAccountId"/> null lets the account choice follow the rule (one paper account is used; several need the setting or this value).</summary>
public sealed record LiveVsPaperQuery(Guid AccountId, Guid? PaperAccountId, DateOnly? From, DateOnly? To, string? Basis, string? ReferenceKind, int Page, int Size) : IQuery<Result<LiveVsPaperView>>;

/// <summary>The execution gap of one live account by strategy or month.</summary>
public sealed record ExecutionGapQuery(Guid AccountId, DateOnly? From, DateOnly? To, string? GroupBy) : IQuery<Result<ExecutionGapView>>;

/// <summary>How many tickets were left out of the report for one reason.</summary>
public sealed record ExclusionCountView(string Reason, int Count);

/// <summary>What stands behind every figure of the report: the basis, how many, which sessions, which reference kind, which paper account and epoch, the pair rule, the exclusion counts and the stated limits. No figure is shown without it.</summary>
public sealed record LiveReportEvidenceView(
    string Basis,
    int N,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    IReadOnlyList<string> ReferenceKinds,
    Guid? PaperAccountId,
    long? PaperEpochId,
    string PairRule,
    IReadOnlyList<ExclusionCountView> Exclusions,
    IReadOnlyList<string> Limits);

/// <summary>The statistics of the paired shortfalls (R on the plan's risk per share; positive is live worse). Every figure is a state and a number only for a value; under the minimum pairs it is <c>insufficient</c> and says so.</summary>
public sealed record LiveVsPaperSummaryView(
    int NPairs,
    MetricView Mean,
    MetricView Sd,
    MetricView Se,
    MetricView Lo95,
    MetricView Hi95,
    MetricView UpperOneSided95,
    MetricView LowerOneSided95,
    MetricView EntryDiffMean,
    MetricView ExitDiffMean,
    MetricView ChargesDiffMean,
    string Verdict,
    double Threshold,
    int MinPairs);

/// <summary>One pair of the same ticket: the live and the paper R and the shortfall split into entry and exit.</summary>
public sealed record LivePairView(long PlanItemId, string Symbol, string Side, DateOnly Date, double LiveR, double PaperR, double ShortfallR, double EntryDiffR, double ExitDiffR);

/// <summary>A ticket that could not be paired, with exactly one reason and what is known about the other side.</summary>
public sealed record LiveExcludedView(long PlanItemId, string Symbol, DateOnly Date, string Reason, string Detail);

/// <summary>One bin of the slippage histogram in basis points; a null edge is open.</summary>
public sealed record LiveHistogramBinView(double? Lo, double? Hi, int Count);

/// <summary>The histogram of live total entry slippage of one reference kind. <see cref="State"/> is <c>value</c>, <c>insufficient</c> (under 10 observations, no bins) or <c>empty</c>.</summary>
public sealed record LiveHistogramView(string Kind, int N, string State, IReadOnlyList<LiveHistogramBinView> Bins);

/// <summary>A point of a cumulative line: the day, the cumulative R and how many pairs are in it.</summary>
public sealed record LiveEquityPointView(DateOnly Date, double Value, int N);

/// <summary>One cumulative-R line over the paired set; <c>unavailable</c> (no points) when an input is missing for any pair.</summary>
public sealed record LiveEquityLineView(string State, string? Reason, IReadOnlyList<LiveEquityPointView> Points);

/// <summary>The three lines over the paired set only, in session order: on plan prices, paper, live.</summary>
public sealed record LiveEquityView(LiveEquityLineView PlanPrices, LiveEquityLineView Paper, LiveEquityLineView Live);

/// <summary>The report: <see cref="State"/> is <c>value</c>, <c>empty</c> (no live round trip yet) or <c>unavailable</c> (the paper record could not be read).</summary>
public sealed record LiveVsPaperView(
    string State,
    LiveVsPaperSummaryView Summary,
    IReadOnlyList<LivePairView> Pairs,
    int PairsTotal,
    int Page,
    int Size,
    IReadOnlyList<LiveExcludedView> Excluded,
    LiveHistogramView Histogram,
    LiveEquityView Equity,
    LiveReportEvidenceView Evidence);

/// <summary>One row of a gap group: a label and a figure that is a value only when its input exists.</summary>
public sealed record LiveGapRowView(string Label, MetricView R);

/// <summary>One group of the execution gap with its rows (plan R, missed, missed unknown, entry, exit, charges, result, overrides), the per-ticket mean of the result and the tickets left out.</summary>
public sealed record LiveGapGroupView(string Key, int N, string State, string Basis, bool Balanced, int Excluded, IReadOnlyList<LiveGapRowView> Rows, MetricView PerTicketMean);

/// <summary>A ticket left out of the gap with the reason.</summary>
public sealed record LiveGapExcludedView(long PlanItemId, string Symbol, DateOnly Date, string Group, string Reason);

/// <summary>The execution gap by group, a total that is built from the same tickets, and the evidence.</summary>
public sealed record ExecutionGapView(string State, string GroupBy, IReadOnlyList<LiveGapGroupView> Groups, LiveGapGroupView Total, IReadOnlyList<LiveGapExcludedView> Excluded, LiveReportEvidenceView Evidence);
