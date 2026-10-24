using System.Text;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>Builders for the report engine tests: a run, a trade, a population that must be valid.</summary>
internal static class ReportFixtures
{
    public static readonly DateOnly Day0 = new(2026, 1, 5);

    public static ReportOptions Small(int minTrades = 3, double strength = 0) => new() { Stats = new CellStatsOptions(PriorStrength: strength, MinTrades: minTrades) };

    public static ReportRun Run(string key, string universe = "U", string? style = "intraday", string? exit = "FIXED_RR", bool costed = false, bool provisional = false, int startOffset = 0, int endOffset = 400) =>
        new(key, universe, Day0.AddDays(startOffset), Day0.AddDays(endOffset), style, exit, costed, provisional);

    public static ReportTrade Trade(string run, string key, double r, string strategy = "ORB", string timeframe = "15m", string side = "BUY", string symbol = "AAA", string[]? baskets = null, int day = 0, int sessions = 1, double? net = null) =>
        new(run, key, strategy, timeframe, side, InstrumentOf(symbol), symbol, new ValueList<string>(baskets ?? []), Day0.AddDays(day), Day0.AddDays(day), r, net, sessions);

    public static Guid InstrumentOf(string symbol) => new(Encoding.UTF8.GetBytes(symbol.PadRight(16, '_'))[..16]);

    /// <summary>Trades of one strategy in one run, one per R value, on consecutive days from <paramref name="firstDay"/>.</summary>
    public static List<ReportTrade> Series(string run, string strategy, IEnumerable<double> rs, int firstDay = 0, string side = "BUY", string[]? baskets = null, string timeframe = "15m")
    {
        var list = new List<ReportTrade>();
        var i = 0;
        foreach (var r in rs)
        {
            list.Add(Trade(run, $"{strategy}-{side}-{timeframe}-{i:D3}", r, strategy, timeframe, side, "AAA", baskets, firstDay + i));
            i++;
        }

        return list;
    }

    public static IEnumerable<double> Repeat(double r, int count) => Enumerable.Repeat(r, count);

    /// <summary>A series with the given number of wins of +1 and losses of -1, wins first.</summary>
    public static IEnumerable<double> WinsAndLosses(int wins, int losses) => Repeat(1, wins).Concat(Repeat(-1, losses));

    public static ReportPopulation Population(IReadOnlyList<ReportRun> runs, IReadOnlyList<ReportTrade> trades, ReportOptions? options = null)
    {
        var result = ReportPopulation.Create(runs, trades, options ?? Small());
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    public static ReportGrouping Group(ReportPopulation population, params ReportDimension[] dimensions)
    {
        var result = population.GroupBy(dimensions);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    public static ReportGroup Only(ReportGrouping grouping, string key) => grouping.Groups.Single(g => g.Key.Text == key);

    public static IEnumerable<Metric> Metrics(ReportGroup group) => Enum.GetValues<ReportMetric>().Select(group.Get);
}
