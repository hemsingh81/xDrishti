namespace XDrishti.Domain.Reporting;

/// <summary>Everything the basket profile reads (BK-06); the engine does no read of its own.</summary>
/// <param name="Members">Every instrument that is in the basket on some date of the window, with its bars.</param>
/// <param name="MembershipAt">The symbols in the basket as of a date (a constant function when only one snapshot exists).</param>
/// <param name="MembershipDates">The dates on which a membership snapshot exists, ascending; fewer than two means a single snapshot (survivorship biased, ISS-22).</param>
/// <param name="Benchmark">The daily closes of the chosen index.</param>
/// <param name="Regimes">The regime code (1 to 9) by date; null or missing means none was recorded.</param>
/// <param name="Window">The window.</param>
public sealed record BasketProfileInput(
    IReadOnlyList<MemberSeries> Members,
    Func<DateOnly, IReadOnlySet<string>> MembershipAt,
    IReadOnlyList<DateOnly> MembershipDates,
    IReadOnlyList<DailyClose> Benchmark,
    IReadOnlyDictionary<DateOnly, int?> Regimes,
    ProfileWindow Window);
