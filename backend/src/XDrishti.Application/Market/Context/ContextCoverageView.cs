namespace XDrishti.Application.Market.Context;

/// <summary>One day (or VIX leg) the derivation did not use, with the reason code and the bars it had.</summary>
public sealed record ContextSkippedView(DateOnly Date, string Reason, int Bars);

/// <summary>
/// The breadth of the owner's tracked shares (P3-01e): <c>Latest</c> is the percent (0 to 100) of the latest day that has one, null when that day was <c>insufficient</c>; <c>State</c> is <c>value</c> or <c>insufficient</c> (null when no day has any);
/// <c>Voters</c>, <c>Universe</c> and <c>Excluded</c> are that day's counts; <c>PendingDays</c> are stored days still without a breadth (the last closed session can still gain bars); <c>InsufficientDates</c> names the thin days. The <c>Label</c> says what it is.
/// </summary>
public sealed record ContextBreadthView(
    DateOnly? LatestDate,
    double? Latest,
    string? State,
    int? Voters,
    int? Universe,
    int? Excluded,
    int ComputedDays,
    int PendingDays,
    int InsufficientDays,
    IReadOnlyList<DateOnly> InsufficientDates,
    string Label);

/// <summary>Days that came from owner-dropped NSE CSVs (fallback D, source <c>nse_csv</c>): index days, VIX closes held in the side table, and the first and last index day.</summary>
public sealed record ContextCsvView(int IndexDays, int VixDays, DateOnly? FirstDate, DateOnly? LastDate);

/// <summary>
/// What the stored market context makes readable (<c>GET /api/data/context</c>, criterion 7). <c>Missing</c> names an unmet prerequisite (<c>index_not_tracked</c>, <c>vix_not_tracked</c>, <c>disabled</c>) with its <c>Fix</c>. <c>AgeSessions</c> is the number of
/// trading sessions the newest day is behind the last closed one; <c>Stale</c> is more than one.
/// </summary>
public sealed record ContextCoverageView(
    bool Enabled,
    string? Missing,
    string? Fix,
    string? IndexSymbol,
    string? VixSymbol,
    int Days,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    int? AgeSessions,
    bool Stale,
    DateOnly? IndexTrendReadableFrom,
    DateOnly? RegimeReadableFrom,
    DateOnly? VixPctReadableFrom,
    DateOnly? RegimeWithVixReadableFrom,
    int VixDays,
    int VixMissingDays,
    int? VixWindowSpanDays,
    bool VixWindowTooWide,
    int GapSessions,
    int SkippedTotal,
    IReadOnlyList<ContextSkippedView> Skipped,
    string Source,
    ContextBreadthView Breadth,
    ContextCsvView Csv);
