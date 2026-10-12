using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Gaps;

/// <summary>
/// A session of an instrument with missing 1-minute bars (ADR 0017 §3). <c>date</c> is the India date, <c>expected</c> the bars a complete session has (from the trading
/// calendar), <c>found</c> what is stored, <c>attempts</c> how often a refetch was made. <c>status</c>: open · filled · unfillable (a refetch added nothing).
/// <c>lastAttemptAt</c> is the latest refetch (null before the first), <c>detectedAt</c> when the gap was found.
/// </summary>
public sealed record GapRow(Guid InstrumentId, string Symbol, string Name, DateOnly Date, int Expected, int Found, int Attempts, string Status, DateTimeOffset? LastAttemptAt, DateTimeOffset DetectedAt);

public sealed record GapPage(IReadOnlyList<GapRow> Items, int Page, int PageSize, int Total);

/// <summary>The gaps list as received (query string values, unchecked): <c>status</c> open|filled|unfillable, <c>instrumentId</c>, <c>page</c> from 1, <c>pageSize</c> up to 200.</summary>
public sealed record GapRequest(string? Status, Guid? InstrumentId, int? Page, int? PageSize);

/// <summary>A checked gaps search; newest sessions first, then by symbol.</summary>
public sealed record GapSearch(GapStatus? Status, Guid? InstrumentId, int Page, int PageSize)
{
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;
}

/// <summary>
/// How many instrument-sessions are in each state (the Dhan phase's gap table): <c>open</c> (missing bars a refetch may still add), <c>unfillable</c> (a refetch added
/// nothing), <c>filled</c>; <c>lastDetectedAt</c> is when a gap was last found (null while there never was one).
/// </summary>
public sealed record GapsOverview(int Open, int Unfillable, int Filled, DateTimeOffset? LastDetectedAt)
{
    public static GapsOverview From(GapSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new GapsOverview(summary.Open, summary.Unfillable, summary.Filled, summary.LastDetectedAt);
    }
}
