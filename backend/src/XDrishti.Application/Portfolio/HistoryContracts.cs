using XDrishti.Application.Portfolio.Returns;

namespace XDrishti.Application.Portfolio;

// Request and response shapes of the portfolio history API (ADR 0020). Money is in rupees; dates are India calendar dates; percentages (Pct, BenchmarkXirr, Alpha) are percent numbers (12.5 = 12.5 %).

/// <summary>
/// The history request as received (query string values, unchecked). <c>From</c>/<c>To</c> are <c>yyyy-MM-dd</c> (default: the first and the last day of the history); <c>Resolution</c> is <c>day</c>, <c>week</c> or <c>month</c> (default: the
/// finest one that gives at most 2,000 points). An account or bucket that does not exist gives an empty history, like the holdings list.
/// </summary>
public sealed record HistoryRequest(Guid? AccountId, Guid? BucketId, string? From, string? To, string? Resolution);

/// <summary>
/// The benchmark of a history. <c>State</c> is <c>ok</c> (the benchmark lines are filled) or <c>unavailable</c> with the <c>Reason</c> in words. <c>Symbol</c> is the canonical symbol and <c>Name</c> the display name of the index (null when the master list has
/// no such index). <c>Tracked</c>: tracking is on; <c>HasBars</c>: 1-minute bars are stored (the day bars follow when the aggregates are built). A screen offers "Track the benchmark" when it is unavailable and not tracked.
/// <c>AsOf</c> is the date of the last index close the benchmark lines were valued at (null when unavailable): the index has no live quote, so the benchmark lines end there and a live point of today (portfolio at today's prices) has no benchmark value.
/// </summary>
public sealed record HistoryBenchmark(string State, Guid? InstrumentId, string? Symbol, string? Name, bool Tracked, bool HasBars, string? Reason, DateOnly? AsOf);

/// <summary>
/// The value history of a scope (all accounts that count in totals, one account, one bucket). <c>Points</c> are in date order at <c>Resolution</c> (<c>day</c>, <c>week</c> or <c>month</c>: the last trading day of the period); <c>Value</c> is what the
/// lots held that day were worth at that day's close (adjusted for splits and bonuses), <c>Invested</c> what they cost, <c>BenchmarkValue</c> the same cash flows in the benchmark index, <c>Nav</c> and <c>BenchmarkNav</c> unit values starting at 100
/// that cash flows do not move. <c>Coverage</c> says how many open holdings have price history; <c>Partial</c> is true when anything is left out (holdings without bars, history that starts late). <c>MaxDrawdown</c> / <c>BenchmarkMaxDrawdown</c>: the largest
/// fall of the NAV measured within the requested range (<c>Pct</c> 12.5 = fell 12.5 %; the peak must lie inside the range too) between the peak on <c>From</c> and the low on <c>To</c>, daily, whatever the resolution; <c>RangeFrom</c> and <c>RangeTo</c> are the first and
/// the last day of that range as returned (null when there is no point). <c>FirstDate</c> is the first day of the whole history (before the range is cut). <c>LastPriceAt</c> is set when the last point is today's live one: the time of the newest price in it.
/// </summary>
public sealed record PortfolioHistory(
    IReadOnlyList<HistoryPoint> Points,
    HistoryCoverage Coverage,
    bool Partial,
    HistoryBenchmark Benchmark,
    Drawdown? MaxDrawdown,
    Drawdown? BenchmarkMaxDrawdown,
    DateOnly? FirstDate,
    DateTimeOffset? LastPriceAt,
    string Resolution,
    DateOnly? RangeFrom,
    DateOnly? RangeTo);

/// <summary>Sets the benchmark: the id of a master-list index.</summary>
public sealed record BenchmarkInput(Guid? InstrumentId);

/// <summary>The benchmark in force (all null when the master list has no such index yet). <c>IsDefault</c>: the owner has not chosen one, Nifty 50 applies.</summary>
public sealed record BenchmarkView(Guid? InstrumentId, string? Symbol, string? Name, bool IsDefault, bool Tracked, bool HasBars);

/// <summary>
/// What a track action did. <c>Requested</c> distinct master-list instruments were asked to be tracked, <c>Added</c> of them are newly tracked (or tracked again), <c>AlreadyTracked</c> were; <c>NotInMaster</c> open holdings in scope
/// are not in the instrument master (nothing to track), <c>Retired</c> instruments are no longer listed and were skipped. <c>TotalTracked</c> is the number tracked now. No job is started: <c>Hint</c> says the next <i>Update data</i> builds the bars.
/// </summary>
public sealed record PortfolioTrackResult(int Requested, int Added, int AlreadyTracked, int NotInMaster, int Retired, int TotalTracked, string Hint);
