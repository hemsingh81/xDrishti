using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.File.Tests.Support;

/// <summary>A seeded generator of definitions that covers every kind of stop, entry, exit policy with its parameter table, overlay, list mode, style and quantity (by number and by parameter name).</summary>
internal static class Corpus
{
    private static readonly string[] Conditions =
    [
        "close > 100",
        "rsi(14) > 55 and close > ema(20)",
        "tf(\"1h\").ema(20) > tf(\"1h\").ema(50)",
        "crosses_above(close, vwap)",
        "close > or_high(15) and close[1] <= or_high(15)",
        "time >= \"09:45\"",
        "not (rsi() < 30)",
        "volume > 1000 and low[1] < low[2]",
    ];

    private static readonly double[] Numbers = [0, 1, 2, 2.5, 14, 0.1, 50, 1e-5, 123456.789, 3];

    private static readonly string[] ParamNames = ["rsi_len", "rr", "or_min", "pull_atr"];

    private static readonly string[] FilterNames = ["not_event_day", "min_turnover"];

    public static IEnumerable<StrategyDefinition> Definitions(int seed, int count)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            yield return One(random, i);
        }
    }

    private static T Pick<T>(Random random, IReadOnlyList<T> items) => items[random.Next(items.Count)];

    private static StrategyDefinition One(Random random, int index)
    {
        var timeframes = new[] { Timeframe.M5, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1 }.Where(_ => random.Next(2) == 0).ToList();
        var sides = random.Next(3) switch { 0 => new List<Side> { Side.Buy }, 1 => [Side.Sell], _ => [Side.Buy, Side.Sell] };
        var parameters = ParamNames.Take(random.Next(5)).Select(n => new StrategyParam(n, Pick(random, Numbers))).ToList();
        return new StrategyDefinition(
            1,
            "S" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            random.Next(2) == 0 ? Style.Intraday : Style.Swing,
            timeframes,
            sides,
            parameters,
            Rules(random),
            random.Next(3) == 0 ? Rules(random) : null,
            random.Next(2) == 0 ? new TimeRules(Time(random), Time(random), Time(random), random.Next(2) == 0 ? random.Next(1, 6) : null) : null,
            FilterNames.Where(_ => random.Next(2) == 0).ToList());
    }

    private static TimeOnly? Time(Random random) => random.Next(3) == 0 ? null : new TimeOnly(random.Next(9, 16), random.Next(0, 60));

    private static SideRules Rules(Random random) => new(
        random.Next(2) == 0 ? List(random) : null,
        List(random),
        random.Next(3) == 0 ? List(random) : null,
        new EntrySpec(Pick(random, Enum.GetValues<EntryKind>())),
        Stop(random),
        Enumerable.Range(0, random.Next(1, 4)).Select(_ => Exit(random)).ToList(),
        random.Next(2) == 0 ? Enumerable.Range(0, random.Next(1, 3)).Select(_ => Overlay(random)).ToList() : null);

    private static ConditionList List(Random random) =>
        new(random.Next(2) == 0 ? ListMode.All : ListMode.Any, Enumerable.Range(0, random.Next(1, 4)).Select(_ => Pick(random, Conditions)).ToList());

    private static Quantity? Maybe(Random random) => random.Next(2) == 0 ? null : Qty(random);

    private static Quantity Qty(Random random) => random.Next(4) == 0 ? Quantity.Named(Pick(random, ParamNames)) : Quantity.Of(Pick(random, Numbers));

    private static StopSpec Stop(Random random) => new(
        Pick(random, Enum.GetValues<StopKind>()),
        random.Next(2) == 0 ? null : Pick(random, new[] { Timeframe.M15, Timeframe.D1 }),
        Maybe(random),
        Maybe(random),
        Maybe(random),
        Maybe(random),
        Maybe(random),
        Maybe(random));

    private static ExitSpec Exit(Random random)
    {
        var policy = Pick(random, Enum.GetValues<ExitPolicy>());
        return new ExitSpec(policy, ExitParameterInfo.Of(policy).Where(p => p.Required || random.Next(2) == 0).Select(p => Parameter(random, p)).ToList());
    }

    private static OverlaySpec Overlay(Random random)
    {
        var kind = Pick(random, Enum.GetValues<OverlayKind>());
        return new OverlaySpec(kind, ExitParameterInfo.Of(kind).Where(p => p.Required || random.Next(2) == 0).Select(p => Parameter(random, p)).ToList());
    }

    private static ExitParameter Parameter(Random random, ExitParameterInfo info) =>
        new(info.Name, Enumerable.Range(0, info.IsList ? random.Next(1, 4) : 1).Select(_ => Qty(random)).ToList());
}
