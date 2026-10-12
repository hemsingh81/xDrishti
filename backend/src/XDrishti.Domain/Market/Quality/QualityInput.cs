using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// What the checks of one instrument need, all read beforehand: <see cref="Sessions"/> (oldest first), the instrument's first and last bar, its 1-minute <see cref="Bars"/> in time order from
/// <see cref="QualityBounds.ReadFrom"/> on, the aggregates it has stored from the bounds on, and the dates a refetch proved unfillable.
/// </summary>
public sealed record QualityInput(
    DateTimeOffset Now,
    TradingCalendar Calendar,
    IReadOnlyList<DateOnly> Sessions,
    DateTimeOffset? FirstBar,
    DateTimeOffset? LastBar,
    IReadOnlyList<Bar> Bars,
    IReadOnlyList<StoredBucket> Stored,
    IReadOnlySet<DateOnly> UnfillableDays);

/// <summary>The verdict on one instrument: <see cref="Status"/> (the worst check), <see cref="Score"/> 0–100, the bars checked and the missing minutes (both in the window) and the checks.</summary>
public sealed record QualityOutcome(string Status, int Score, int BarsChecked, int MissingMinutes, QualityChecks Checks);
