namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// What <see cref="PortfolioHistoryCalculator"/> built: the daily points, how much of the open holdings they cover, whether anything was left out (<see cref="Partial"/>), whether the benchmark could be compared and, when the last point
/// is today's live one, the time of the newest price in it. <see cref="BenchmarkAsOf"/> is the date of the last index close the benchmark was valued at: the benchmark ends there, so on a live day (the index has no live quote) its
/// lines stop at the last close instead of showing a flat point.
/// </summary>
public sealed record PortfolioSeries(
    IReadOnlyList<HistoryPoint> Points,
    HistoryCoverage Coverage,
    bool Partial,
    BenchmarkAvailability Benchmark,
    DateTimeOffset? LastPriceAt,
    DateOnly? BenchmarkAsOf = null);
