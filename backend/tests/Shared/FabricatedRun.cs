using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Tests.Shared;

/// <summary>
/// Run records made by hand for the tests of the export (spec P3-04b1): trades and instruments of the invented F-days, put together by the real <see cref="RunAssembler"/> so that the run key, the totals and the order are the ones a
/// run gives. Nothing here is read from <c>data/</c>; the symbols are invented and cannot collide with a word of <see cref="ForbiddenWords"/>.
/// </summary>
internal static class FabricatedRun
{
    public static readonly Guid IdA = new("00000000-0000-0000-0000-00000000000a");

    public static readonly Guid IdB = new("00000000-0000-0000-0000-00000000000b");

    public static readonly Guid IdC = new("00000000-0000-0000-0000-00000000000c");

    public static readonly Guid Basket = new("11111111-2222-3333-4444-555555555555");

    public static readonly DateOnly Monday = new(2026, 3, 2);

    public static string DefaultFills => FillRules.Default.ToCanonicalJson();

    public static StrategyRecord Strategy(string id = "ORB") => new(
        id,
        new Dictionary<string, double> { ["rr"] = 2, ["or_min"] = 15 },
        "{\"id\":\"" + id + "\"}",
        new string('1', 64),
        Timeframe.M15,
        [Side.Buy, Side.Sell]);

    public static UniverseRecord List(params (Guid Id, string Symbol)[] members) =>
        new(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [.. members.Select(m => new UniverseMember(m.Id, m.Symbol))]);

    public static UniverseRecord BasketOf(params (Guid Id, string Symbol)[] members) =>
        new(UniverseKind.Basket, Basket, "Banks", UniverseLabels.MembersAsOfToday, [.. members.Select(m => new UniverseMember(m.Id, m.Symbol))]);

    /// <summary>An instrument that ran from <paramref name="first"/> to <paramref name="last"/>, with data facts, an adjustment-free replay and zero counters.</summary>
    public static InstrumentRun Ran(Guid id, string symbol, DateOnly first, DateOnly last) => new(
        id, symbol, null, null,
        first, last, first, new DateSpan(first, last),
        750, 750, 0, 0, new string('a', 64),
        0.05m, false,
        [], new SessionCounts(2, 0, 0, 0, 0), [], false, [],
        [ScanCounters.Empty(Side.Buy), ScanCounters.Empty(Side.Sell)], WalkCounters.Empty, RunFates.Empty,
        0, 0);

    /// <summary>An instrument that was read and skipped for <paramref name="reason"/>.</summary>
    public static InstrumentRun Skipped(Guid id, string symbol, SkipReason reason, string? detail = null) =>
        InstrumentRun.NotFound(id) with { Symbol = symbol, Skipped = reason, SkipDetail = detail };

    public static SimTrade Trade(
        Guid instrument,
        string symbol,
        Side side,
        DateOnly date,
        ExitReason exit = ExitReason.Target,
        int entryMinute = 600,
        double entry = 100.5,
        double stop = 98,
        double? target = 105.5,
        double exitPrice = 105.5,
        double rExact = 2,
        double rFilled = 1.95,
        FillBasis fills = FillBasis.Modelled,
        string strategy = "ORB")
    {
        var entryEpoch = Day.Epoch(date, entryMinute);
        return new SimTrade(
            SimTrade.CurrentSchemaVersion, strategy, instrument, symbol, side, date, entryEpoch - 60, Timeframe.M15, new Bar(entryEpoch - 960, 99, 100.5, 99, 100.5, 15000), 15, true,
            entryEpoch, entry, stop, target, entryEpoch + 600, exitPrice, exit, rExact, 0.4, 2.1, fills, CostBasis.None, null,
            rFilled, rFilled - rExact, entry + 0.05, exitPrice - 0.05, EntryFill.AtOpen, false);
    }

    /// <summary>Puts the instruments and trades together with the real assembler. <paramref name="instruments"/> are in universe order.</summary>
    public static SimulationRun Assemble(UniverseRecord universe, IReadOnlyList<InstrumentRun> instruments, IReadOnlyList<SimTrade>? trades = null, IReadOnlyList<SimTrade>? openAtEnd = null, string? fills = null, DateOnly? from = null, DateOnly? to = null, string strategy = "ORB")
    {
        var tradeList = trades ?? [];
        var openList = openAtEnd ?? [];
        var replays = instruments.Select(i => new InstrumentReplay(i, [.. tradeList.Where(t => t.InstrumentId == i.InstrumentId)], [.. openList.Where(t => t.InstrumentId == i.InstrumentId)])).ToList();
        var header = new RunHeader(Strategy(strategy), "sim-2", from ?? Monday, to ?? new DateOnly(2026, 4, 30), ReplayOptions.Default, false, fills ?? DefaultFills, universe, new TradingCalendar());
        return RunAssembler.Assemble(header, replays);
    }

    /// <summary>
    /// The run of the golden files: two instruments that ran (one with a split, a flagged day, session notes and counters on both sides), one skipped, a Buy at the target, a Sell at the stop with slippage and an <c>EndOfData</c> trade.
    /// </summary>
    public static SimulationRun Standard()
    {
        var a = Ran(IdA, "NSE:AAA", new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30)) with
        {
            Adjustments = [new AppliedAdjustment(new DateOnly(2025, 6, 2), CorporateActionKind.Split, 1, 2, 0.5m, false)],
            Sessions = new SessionCounts(500, 0, 1, 1, 1),
            Notes = [new SessionNote(new DateOnly(2025, 11, 1), SessionExclusion.SpecialSession, 60, 0), new SessionNote(new DateOnly(2025, 9, 3), SessionExclusion.ShortSession, 300, 375)],
            Discontinuities = [new DiscontinuityNote(new DateOnly(2025, 6, 2), 0.52)],
            Scans =
            [
                new ScanCounters(Side.Buy, 187500, 3, 0, 500, 0, 0, 0, 480, 470, 12, 4, 1, 0, 0, 0, [new ConditionCounter("buy.trigger[0]", 16, 464, 0, 0)]),
                new ScanCounters(Side.Sell, 187500, 3, 0, 500, 0, 0, 0, 480, 470, 9, 3, 0, 0, 0, 0, [new ConditionCounter("sell.trigger[0]", 12, 468, 0, 0)]),
            ],
            Walk = new WalkCounters(21, 3, 0, 0, 0, 1, 1, 0, 1, 0, 0, 0, 40, 18, 3, 0, 0, 0, 0, 0),
            Fates = new RunFates(1, 2, 1, 2, 0, 0, 0, 15),
            CandidatesInWindow = 21,
            TradesInWindow = 3,
        };
        var b = Ran(IdB, "NSE:M&M", new DateOnly(2024, 12, 18), new DateOnly(2026, 4, 30)) with { TickSize = 0.1m };
        var c = Skipped(IdC, "NSE:CCC", SkipReason.NoBars);

        var trades = new[]
        {
            Trade(IdA, "NSE:AAA", Side.Buy, Monday),
            Trade(IdB, "NSE:M&M", Side.Sell, Monday.AddDays(1), ExitReason.Stop, entryMinute: 615, entry: 200, stop: 204, target: 192, exitPrice: 204, rExact: -1, rFilled: -1.1),
        };
        var open = new[]
        {
            Trade(IdA, "NSE:AAA", Side.Buy, Monday.AddDays(2), ExitReason.EndOfData, entryMinute: 900, target: null, exitPrice: 101, rExact: 0.2, rFilled: 0.15),
        };
        return Assemble(List((IdA, "NSE:AAA"), (IdB, "NSE:M&M"), (IdC, "NSE:CCC")), [a, b, c], trades, open, from: new DateOnly(2024, 4, 1));
    }

    /// <summary>A run with <paramref name="trades"/> trades of one instrument, a minute apart on each of the sessions: for the size and the memory tests (no instrument counters).</summary>
    public static SimulationRun Large(int trades)
    {
        var a = Ran(IdA, "NSE:AAA", new DateOnly(2024, 4, 1), new DateOnly(2026, 4, 30));
        var list = new List<SimTrade>(trades);
        var date = Monday;
        for (var i = 0; i < trades; i++)
        {
            if (i % 300 == 0 && i > 0)
            {
                date = date.AddDays(1);
            }

            list.Add(Trade(IdA, "NSE:AAA", i % 2 == 0 ? Side.Buy : Side.Sell, date, entryMinute: 555 + (i % 300), rExact: 2 - (i % 7 * 0.37), rFilled: 1.9 - (i % 7 * 0.37)));
        }

        return Assemble(List((IdA, "NSE:AAA")), [a], list);
    }
}
