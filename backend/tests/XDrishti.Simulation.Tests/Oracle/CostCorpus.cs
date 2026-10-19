using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The cost runs of P3-08c, made once and shared by the tests that read them: (1) the oracle author's generator, <see cref="OracleCostSeries"/>, on its synthetic sheets, each case costed by the oracle and by the engine; (2) the chain
/// of the spec (AC17): the walks of the seeded days, daily series and swing series (both fill modes) are walked by the oracle, the oracle's trade is costed by the oracle, the engine's trade by <see cref="TradeCostApplier"/>, on the TEST
/// sheet in its three rounding modes and both schedules and on the fixture sheet, in four capitals from 10,000 to 1,00,00,000, both sizing profiles and four limits (the third equal to the trade's own charge in R, the fourth just under it).
/// A run is a pure function of the seeds and is folded in a fixed order, so two runs give the same summary.
/// </summary>
internal static class CostCorpus
{
    internal const string SeriesName = "OracleCostSeries";

    internal const string ChainName = "WalkChain";

    /// <summary>The cases of the author's generator per category: 1,000, and more for the category whose equal-to-the-limit sub-case is a quarter of it (the floor of 500 needs more than 2,000).</summary>
    internal static int SeedsOf(OracleCostCategory category) => category == OracleCostCategory.CostOverLimit ? 2400 : 1000;

    /// <summary>The ratio fields (<c>CostR</c> and <c>RNet</c>) seen by all the runs below.</summary>
    internal static readonly RatioStats Stats = new();

    internal static readonly decimal[] Capitals = [10_000m, 100_000m, 1_000_000m, 10_000_000m];

    private static readonly SizingProfile[] Profiles = [SizingProfile.Standard, SizingProfile.Conservative];

    private static readonly Lazy<CostTally> SeriesLazy = new(RunSeries);

    private static readonly Lazy<CostTally> ChainLazy = new(RunChain);

    internal static CostTally SeriesRun => SeriesLazy.Value;

    internal static CostTally ChainRun => ChainLazy.Value;

    internal static IReadOnlyList<CostTally> All => [SeriesRun, ChainRun];

    /// <summary>The sheets the chain is costed on: the TEST sheet in each rounding mode (schedule A in force on the seeded days, B from 2026-03-03), the same with schedule B in force from the start, and the fixture sheet.</summary>
    internal static IReadOnlyList<CostSheet> ChainSheets { get; } = BuildSheets();

    private static List<CostSheet> BuildSheets()
    {
        var sheets = new List<CostSheet>();
        foreach (var rounding in Enum.GetValues<ChargeRounding>())
        {
            sheets.Add(TestCostSheet.WithRounding(rounding));
            sheets.Add(TestCostSheet.Of("test-synthetic-b", TestCostSheet.Schedule(new DateOnly(2000, 1, 1), TestCostSheet.IntradayB, TestCostSheet.DeliveryB, rounding: rounding)));
        }

        sheets.Add(FixtureSheet.Sheet);
        return sheets;
    }

    /// <summary>Runs the author's generator: <see cref="SeedsOf"/> cases (seeds 1 up) of each of the 13 categories.</summary>
    internal static CostTally RunSeries(Func<OracleCostCategory, int> count)
    {
        var categories = Enum.GetValues<OracleCostCategory>();
        var parts = new CostTally[categories.Length];
        Parallel.For(0, categories.Length, index =>
        {
            var category = categories[index];
            var part = new CostTally(SeriesName);
            for (long seed = 1; seed <= count(category); seed++)
            {
                var source = OracleCostSeries.Of(category, seed);
                part.Add(CostComparison.Run(source, Stats, $"{category} seed {seed}"), $"{category} seed {seed}");
                part.Trades++;
            }

            parts[index] = part;
        });
        var tally = new CostTally(SeriesName);
        foreach (var part in parts)
        {
            tally.Merge(part);
        }

        return tally;
    }

    private static CostTally RunSeries() => RunSeries(SeedsOf);

    // ---- the chain

    /// <summary>The engine's gross trades for the candidates, in order, through the gate when the rules hold overnight (null for a no-trade).</summary>
    internal static List<SimTrade?> EngineTrades(IReadOnlyList<Bar> bars, IReadOnlyList<EntryCandidate> candidates, WalkRules rules)
    {
        var trades = new List<SimTrade?>();
        if (rules.Hold is not null)
        {
            var gated = PositionGate.Apply(candidates, c => TradeWalker.Walk(c, bars, rules));
            gated.IsSuccess.ShouldBeTrue("the gate failed");
            trades.AddRange(gated.Value.Select(one => one.Walk.Trade));
        }
        else
        {
            foreach (var candidate in candidates)
            {
                var walked = TradeWalker.Walk(candidate, bars, rules);
                walked.IsSuccess.ShouldBeTrue("the walk failed");
                trades.Add(walked.Value.Trade);
            }
        }

        return trades;
    }

    /// <summary>A sheet whose second schedule starts on the exit date of a held trade: the entry leg is charged on schedule A and the exit leg on schedule B (the swing trade across a schedule change).</summary>
    private static CostSheet Crossing(DateOnly exitDate, ChargeRounding rounding) =>
        TestCostSheet.Of(
            "test-synthetic-x",
            TestCostSheet.Schedule(new DateOnly(2000, 1, 1), TestCostSheet.IntradayA, TestCostSheet.DeliveryA, rounding: rounding),
            TestCostSheet.Schedule(exitDate, TestCostSheet.IntradayB, TestCostSheet.DeliveryB, rounding: rounding));

    /// <summary>The limit of the chain for a configuration: 0.10, 0.01, exactly the trade's own charge in R, or just under it.</summary>
    internal static decimal Limit(int option, decimal costR) => option switch
    {
        0 => 0.10m,
        1 => 0.01m,
        2 when costR > 0m => costR,
        3 when costR > 0m => costR * 0.999999999m,
        _ => 0.10m,
    };

    private static CostTally RunChain() =>
        RunChain(ComparisonCases.Intraday(exact: false).Concat(ComparisonCases.Intraday(exact: true))
            .Concat(ComparisonCases.Daily(exact: false)).Concat(ComparisonCases.Daily(exact: true))
            .Concat(ComparisonCases.Swing(exact: false)).Concat(ComparisonCases.Swing(exact: true)));

    /// <summary>The chain over the given walk cases (the whole corpus in the default run; a few cases in the reproducibility test).</summary>
    internal static CostTally RunChain(IEnumerable<ComparisonCase> cases)
    {
        var parts = cases.AsParallel().AsOrdered().Select(ChainOne).ToList();
        var tally = new CostTally(ChainName);
        foreach (var part in parts)
        {
            tally.Merge(part);
        }

        return tally;
    }

    private static CostTally ChainOne(ComparisonCase series)
    {
        var part = new CostTally(ChainName);
        var index = 0;
        foreach (var group in series.Groups)
        {
            var oracleResults = group.Oracle.Run(OracleFaults.None).Results;
            var engineTrades = EngineTrades(series.Bars, group.EngineCandidates, group.EngineRules);
            var segment = group.EngineRules.Hold is null ? OracleCostSegment.IntradayEquity : OracleCostSegment.Delivery;
            for (var i = 0; i < oracleResults.Count; i++)
            {
                var oracleTrade = oracleResults[i].Trade;
                var engineTrade = engineTrades[i];
                if (oracleTrade is null || engineTrade is null)
                {
                    oracleTrade.ShouldBeNull("the walks agree (WalkComparisonTests); a costing chain starts from the same trades");
                    engineTrade.ShouldBeNull();
                    continue;
                }

                var exitDate = OracleClock.DateOf(oracleTrade.ExitEpochSeconds);
                var sheets = exitDate > oracleTrade.TradeDate ? [.. ChainSheets, Crossing(exitDate, (ChargeRounding)(index % 3))] : ChainSheets;
                part.Trades++;
                for (var s = 0; s < sheets.Count; s++)
                {
                    var capital = Capitals[(index + (int)series.Seed + s) % Capitals.Length];
                    var profile = Profiles[((index + (int)series.Seed + s) / Capitals.Length) % Profiles.Length];
                    var sizing = CostAdapter.Sizing(profile);
                    var sheet = sheets[s];
                    var oracleSheet = CostAdapter.Sheet(sheet);
                    var where = $"{series.Generator} seed {series.Seed} {series.Mode} trade {index} sheet {s}";
                    var open = ReferenceCosting.Cost(CostAdapter.Input(oracleTrade, segment, capital, 100m), oracleSheet, sizing);
                    var limit = Limit((index + s + (int)series.Seed) % 4, open.CostR);
                    var input = CostAdapter.Input(oracleTrade, segment, capital, limit);
                    var outcome = CostComparison.Run(input, engineTrade, oracleSheet, sheet, sizing, profile, Stats, where);
                    part.Add(outcome, where);
                }

                index++;
            }
        }

        return part;
    }
}
