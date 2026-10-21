using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// The runs of the result-store tests (spec P3-10): a 20-instrument universe whose trades are put together by the real <see cref="RunAssembler"/> (not a golden file written by the store), gross or costed by the invented sheet <c>fixture-a</c>.
/// The run key follows the inputs (strategy id, window), so a test that needs its own key changes <c>to</c> or the strategy id. The trades cover what the columns must hold: both sides, every exit reason, a null target, a negative zero,
/// a swing trade, a trade with two baskets and a symbol with a dot or a hyphen.
/// </summary>
internal static class StoredRunFixtures
{
    public static readonly Guid BasketOne = new("aaaaaaaa-0000-0000-0000-000000000001");

    public static readonly Guid BasketTwo = new("aaaaaaaa-0000-0000-0000-000000000002");

    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static readonly ExitReason[] Exits = [ExitReason.Stop, ExitReason.Target, ExitReason.SquareOff, ExitReason.MaxHold, ExitReason.TimeStop];

    public static Guid InstrumentId(int index) => new($"00000000-0000-0000-0000-{index + 1:x12}");

    public static string Symbol(int index) => (index % 7) switch
    {
        3 => $"NSE:SYM{index:00}-X",
        4 => $"NSE:S.Y{index:00}",
        _ => $"NSE:SYM{index:00}",
    };

    /// <summary>
    /// The run: <paramref name="instruments"/> instruments, <paramref name="tradesEach"/> trades of each (and one open-at-end trade for every fifth instrument), assembled by the real assembler.
    /// </summary>
    public static SimulationRun Run(bool costed = false, int instruments = 20, int tradesEach = 6, DateOnly? to = null, string strategy = "ORB", bool fixtureEdges = true, FillRules? fills = null)
    {
        var context = TestCostSheet.Context(sheet: FixtureSheet.Sheet);
        var ran = new List<InstrumentRun>();
        var trades = new List<SimTrade>();
        var open = new List<SimTrade>();
        for (var i = 0; i < instruments; i++)
        {
            var id = InstrumentId(i);
            var symbol = Symbol(i);
            var count = 0;
            for (var j = 0; j < tradesEach; j++)
            {
                var side = (i + j) % 2 == 0 ? Side.Buy : Side.Sell;
                var entry = 100.5 + i;
                var trade = FabricatedRun.Trade(
                    id, symbol, side, Monday.AddDays(j), Exits[(i + j) % Exits.Length], entryMinute: 560 + ((i * 7 + j * 3) % 200), entry: entry, stop: side == Side.Buy ? entry - 2.5 : entry + 2.5,
                    target: (i + j) % 3 == 0 ? null : entry + 5, exitPrice: entry + (j - 2), rExact: (j - 2) * 0.4, rFilled: ((j - 2) * 0.4) - 0.05, strategy: strategy);
                count += Add(trades, trade, costed, context);
            }

            if (i % 5 == 0)
            {
                var eod = FabricatedRun.Trade(id, symbol, Side.Buy, Monday.AddDays(tradesEach + 1), ExitReason.EndOfData, entryMinute: 900, target: null, exitPrice: 101, rExact: 0.2, rFilled: 0.15, strategy: strategy);
                count += Add(open, eod, costed, context);
            }

            ran.Add(FabricatedRun.Ran(id, symbol, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30)) with { TradesInWindow = count, CandidatesInWindow = count + 2 });
        }

        var members = Enumerable.Range(0, instruments).Select(i => (InstrumentId(i), Symbol(i))).ToArray();
        var run = Assemble(strategy, FabricatedRun.List(members), ran, trades, open, to ?? new DateOnly(2026, 4, 30), costed ? CostRecord.Of(context) : null, fills);
        return fixtureEdges ? WithEdges(run) : run;
    }

    /// <summary>The same trades, replicated to <paramref name="trades"/> rows with distinct keys (distinct instruments and dates), costed or gross: for the size and time measurement.</summary>
    public static SimulationRun Synthetic(int trades, bool costed)
    {
        var context = TestCostSheet.Context(sheet: FixtureSheet.Sheet);
        var ran = new List<InstrumentRun>();
        var list = new List<SimTrade>(trades);
        var instruments = Math.Max(1, trades / 400);
        var made = 0;
        for (var i = 0; i < instruments && made < trades; i++)
        {
            var id = InstrumentId(i);
            var symbol = "NSE:S" + i.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
            var count = 0;
            for (var d = 0; d < 200 && made < trades; d++)
            {
                for (var k = 0; k < 2 && made < trades; k++)
                {
                    var side = k == 0 ? Side.Buy : Side.Sell;
                    var entry = 100.5 + (i % 50);
                    var trade = FabricatedRun.Trade(
                        id, symbol, side, Monday.AddDays(d), Exits[(i + d + k) % Exits.Length], entryMinute: 560 + ((d * 5 + k) % 300), entry: entry, stop: side == Side.Buy ? entry - 2.5 : entry + 2.5,
                        target: d % 3 == 0 ? null : entry + 5, exitPrice: entry + ((d % 5) - 2), rExact: ((d % 5) - 2) * 0.4, rFilled: (((d % 5) - 2) * 0.4) - 0.05);
                    count += Add(list, trade, costed, context);
                    made++;
                }
            }

            ran.Add(FabricatedRun.Ran(id, symbol, new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30)) with { TradesInWindow = count, CandidatesInWindow = count });
        }

        var members = Enumerable.Range(0, ran.Count).Select(i => (ran[i].InstrumentId, ran[i].Symbol)).ToArray();
        return Assemble("ORB", FabricatedRun.List(members), ran, list, [], new DateOnly(2026, 4, 30), costed ? CostRecord.Of(context) : null, null);
    }

    private static int Add(List<SimTrade> into, SimTrade trade, bool costed, CostContext context)
    {
        if (!costed)
        {
            into.Add(trade);
            return 1;
        }

        var applied = TradeCostApplier.Apply(trade, context, CostSegment.IntradayEquity);
        if (applied.Value.Trade is not { } costedTrade)
        {
            return 0;
        }

        into.Add(costedTrade);
        return 1;
    }

    private static SimulationRun Assemble(string strategy, UniverseRecord universe, List<InstrumentRun> ran, List<SimTrade> trades, List<SimTrade> open, DateOnly to, CostRecord? costs, FillRules? fills)
    {
        var replays = ran.Select(i => new InstrumentReplay(i, [.. trades.Where(t => t.InstrumentId == i.InstrumentId)], [.. open.Where(t => t.InstrumentId == i.InstrumentId)])).ToList();
        var header = new RunHeader(FabricatedRun.Strategy(strategy), "sim-6", new DateOnly(2026, 1, 1), to, ReplayOptions.Default, false, (fills ?? FillRules.Default).ToCanonicalJson(), universe, new TradingCalendar(), costs);
        return RunAssembler.Assemble(header, replays);
    }

    /// <summary>The edge cases of the columns, set on trades of the assembled run (none of them touches the trade key): a negative zero, a swing trade, two baskets, one basket, epoch seconds near the 32-bit limit, an entry snapshot text.</summary>
    private static SimulationRun WithEdges(SimulationRun run)
    {
        var trades = run.Trades.ToList();
        trades[0] = trades[0] with { Trade = trades[0].Trade with { RExact = -0.0, RFilled = -0.0, SlippageR = 0.0 } };
        trades[1] = trades[1] with { Trade = trades[1].Trade with { SessionsHeld = 3, SessionsUnseen = 1, ExitEpochSeconds = trades[1].Trade.ExitEpochSeconds + (2 * 86_400) } };
        trades[2] = trades[2] with { BasketIds = [BasketOne, BasketTwo] };
        trades[3] = trades[3] with { BasketIds = [BasketTwo] };
        trades[4] = trades[4] with { Trade = trades[4].Trade with { EntryEpochSeconds = 2_147_483_000, ExitEpochSeconds = 2_147_483_600, TickFallback = true } };
        trades[5] = trades[5] with { Trade = trades[5].Trade with { EntrySnapshotJson = "{\"k\":\"v\"}", TriggerBarComplete = false } };
        return run with { Trades = trades };
    }
}
