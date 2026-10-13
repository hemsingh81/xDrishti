namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// One day of the portfolio history (rupees; <c>Nav</c> and <c>BenchmarkNav</c> start at 100). <c>Value</c> is what the lots held that day were worth at the day's close (adjusted prices), <c>Invested</c> what
/// they cost. <c>BenchmarkValue</c> is the same cash flows put into the benchmark index; it and <c>BenchmarkNav</c> are null when the benchmark has no history for the series.
/// </summary>
public sealed record HistoryPoint(DateOnly Date, decimal Value, decimal Invested, decimal? BenchmarkValue, decimal Nav, decimal? BenchmarkNav);
