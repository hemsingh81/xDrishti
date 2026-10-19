using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;

namespace XDrishti.SimExport.Tests.Support;

/// <summary>
/// The runs and the days of the card tests (spec P3-04c): invented trades on the F-days, put together by the real assembler. Symbols are from a fixed list that cannot collide with a word of <see cref="ForbiddenWords"/>. The strategy of
/// a card run is the real ORB definition text (the golden of P3-04), so that the rule view has something to read.
/// </summary>
internal static class CardFixtures
{
    /// <summary>The canonical definition text of ORB v1 (the golden file <c>orb-v1.definition.golden.txt</c> of the simulation tests, without its header).</summary>
    public const string OrbDefinition =
        "{\"schema\":1,\"id\":\"ORB\",\"style\":\"intraday\",\"timeframes\":[\"5m\",\"15m\"],\"sides\":[\"BUY\",\"SELL\"],\"params\":[{\"name\":\"or_min\",\"value\":15}],"
        + "\"buy\":{\"trigger\":{\"mode\":\"all\",\"items\":[\"close > or_high(or_min) and close[1] <= or_high(or_min)\"]},\"entry\":{\"kind\":\"at_close\"},\"stop\":{\"kind\":\"or_opposite\",\"minutes\":\"or_min\"},\"exits\":[{\"policy\":\"FIXED_RR\",\"parameters\":[{\"name\":\"rr\",\"values\":[2]}]}]},"
        + "\"sell\":{\"trigger\":{\"mode\":\"all\",\"items\":[\"close < or_low(or_min) and close[1] >= or_low(or_min)\"]},\"entry\":{\"kind\":\"at_close\"},\"stop\":{\"kind\":\"or_opposite\",\"minutes\":\"or_min\"},\"exits\":[{\"policy\":\"FIXED_RR\",\"parameters\":[{\"name\":\"rr\",\"values\":[2]}]}]},"
        + "\"time_rules\":{\"first_entry\":\"09:30\",\"last_entry\":\"11:30\",\"square_off\":\"15:15\"},\"filters\":[]}";

    public static readonly DateOnly Date = ReplayDays.D2;

    public static StrategyRecord Orb() => new(
        "ORB", new Dictionary<string, double> { ["or_min"] = 15, ["rr"] = 2 }, OrbDefinition, new string('c', 64), Timeframe.M15, [Side.Buy, Side.Sell]);

    /// <summary>A BUY on the F1 day: entry 100.50 at 09:45, stop 98, target 105.50, exit at the target at 11:10; filled 100.55 and 105.45. With <paramref name="factor"/> 0.5 every price and the bars are halved (a split after the trade day).</summary>
    public static (SimTrade Trade, Bar[] Bars) BuyAtTarget(double factor = 1.0)
    {
        var bars = factor == 1.0 ? ReplayDays.F1(Date) : [.. ReplayDays.F1(Date).Select(b => b with { Open = b.Open * factor, High = b.High * factor, Low = b.Low * factor, Close = b.Close * factor, Volume = (long)(b.Volume / factor) })];
        var trade = CostTrades.Make(Side.Buy, Date, 100.50 * factor, 98 * factor, 105.50 * factor, 100.55 * factor, 105.45 * factor, null, ExitReason.Target, 585, 670) with
        {
            Symbol = "NSE:KESTREL",
            InstrumentId = Instrument,
            DecisionEpochSeconds = Day.Epoch(Date, 585),
            TriggerBar = new Bar(Day.Epoch(Date, 570), 99 * factor, 100.50 * factor, 98 * factor, 100.50 * factor, 15000),
            TargetPrice = 105.50 * factor,
            MaeR = 0.4,
            MfeR = 2.1,
        };
        return (trade, bars);
    }

    /// <summary>A SELL on a day with a missing minute: entry 200 at 10:00, stop 204 above the day's range, target 192, stopped out at 10:30; filled 199.9 and 204.1.</summary>
    public static (SimTrade Trade, Bar[] Bars) SellAtStop()
    {
        var bars = Day.Dense(Date).Start(200).Set("10:15 200.00/200.50/199.50/200.00").Absent("10:20").Bars();
        var trade = CostTrades.Make(Side.Sell, Date, 200, 204, 204, 199.9, 204.1, null, ExitReason.Stop, 600, 630) with
        {
            Symbol = "NSE:HERON",
            InstrumentId = Instrument,
            DecisionEpochSeconds = Day.Epoch(Date, 600),
            TriggerBar = new Bar(Day.Epoch(Date, 585), 200, 200, 200, 200, 15000),
            TargetPrice = 192,
        };
        return (trade, bars);
    }

    public static readonly Guid Instrument = CostTrades.Instrument;

    /// <summary>The card of a trade over its bars; the instrument has the adjustments given (a split ex-dated after the trade day when <paramref name="split"/>).</summary>
    public static (SimulationRun Run, TradeCardData Data) Card(SimTrade trade, Bar[] bars, bool split = false, CostRecord? costs = null, StrategyRecord? strategy = null)
    {
        var first = Date.AddDays(-30);
        var instrument = FabricatedRun.Ran(trade.InstrumentId, trade.Symbol, first, Date.AddDays(60)) with
        {
            Adjustments = split ? [new AppliedAdjustment(Date.AddDays(1), CorporateActionKind.Split, 1, 2, 0.5m, false)] : [],
            BarsSha256 = "0123456789abcdef" + new string('a', 48),
        };
        var run = FabricatedRun.Assemble(FabricatedRun.List((trade.InstrumentId, trade.Symbol)), [instrument], [trade], costs: costs) with { Strategy = strategy ?? Orb() };
        var opening = bars.Cast<Bar?>().FirstOrDefault(b => b!.Value.EpochSeconds == Day.Epoch(Date, 555));
        Bar? raw = split && opening is { } o ? o with { Open = o.Open / 0.5, High = o.High / 0.5, Low = o.Low / 0.5, Close = o.Close / 0.5, Volume = (long)(o.Volume * 0.5) } : null;
        return (run, new TradeCardData(run.Trades[0], instrument, bars, opening, raw));
    }

    /// <summary>The card of a costed SELL: sized and charged by the real applier with the TEST sheet.</summary>
    public static (SimulationRun Run, TradeCardData Data) CostedSell()
    {
        var (trade, bars) = SellAtStop();
        var context = TestCostSheet.Context();
        var costed = TradeCostApplier.Apply(trade, context, CostSegment.IntradayEquity).Value.Trade ?? throw new InvalidOperationException("The fabricated trade was skipped by costing.");
        return Card(costed, bars, costs: CostRecord.Of(context));
    }

    /// <summary>The golden pair: a BUY at the target on a split instrument and a SELL at the stop on a day with a missing minute.</summary>
    public static IReadOnlyList<(string Name, SimulationRun Run, TradeCardData Data)> Goldens()
    {
        var (buy, buyBars) = BuyAtTarget(0.5);
        var (sell, sellBars) = SellAtStop();
        var (buyRun, buyData) = Card(buy, buyBars, split: true);
        var (sellRun, sellData) = Card(sell, sellBars);
        return [("buy-at-target-split", buyRun, buyData), ("sell-at-stop-missing-minute", sellRun, sellData)];
    }

    public static RunLabelSet Labels(SimulationRun run) => RunLabels.Of(run, FillRules.Default);

    public static string Render(SimulationRun run, TradeCardData data) => Sinks.Render(sink => TradeCardHtml.Write(sink, data, run, Labels(run)));
}
