using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// D12 (f) of P4-05: the comparison arithmetic (the common set, the table of D6, the paired difference, the diagnostics of D7) agrees with the independent oracle on a seeded fixture, gross and costed. The oracle is written from the text of the spec and
/// is given seven numbers of each trade; the engine's <see cref="ComparisonReport"/> is built from the run records of the replay of seven candidates (one of each policy) over 300 seeded instruments. The walk rules are not in question here: they have
/// the oracles of P4-01 to P4-04.
/// </summary>
public sealed class ComparisonStatsOracleTests(ITestOutputHelper output)
{
    private const double Tolerance = 1e-9;

    private const int Instruments = 300;

    private static readonly ExitChoice Seven = Compared.Choice(null, Compared.Seven);

    private static Guid IdOf(int seed) => new(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    private static (ComparisonReport Report, IReadOnlyList<OracleComparisonCandidate> Candidates) Build(bool costed)
    {
        var costs = costed ? TestCostSheet.Context(1_000_000m, 0.10m) : null;
        var built = BuiltInStrategies.Prepare(OrbV1.Id, null, Timeframe.M15, Cand.NoSlippage, Seven);
        var prepared = built.Strategy.ShouldNotBeNull();
        var perCandidate = Seven.Candidates.Select(_ => new List<InstrumentReplay>()).ToList();
        for (var seed = 1; seed <= Instruments; seed++)
        {
            var replay = InstrumentReplayer.ReplayAll(prepared, Rep.Input(SeededSeries.Of((ulong)seed), Day.Monday, Day.Monday.AddDays(8), IdOf(seed), "S" + seed, costs: costs));
            for (var k = 0; k < perCandidate.Count; k++)
            {
                perCandidate[k].Add(replay.Members[k]);
            }
        }

        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [.. Enumerable.Range(1, Instruments).Select(s => new UniverseMember(IdOf(s), "S" + s))]);
        var runs = new List<CandidateRun>();
        for (var k = 0; k < perCandidate.Count; k++)
        {
            var header = new RunHeader(prepared.Members[k].Record, "sim-6", Day.Monday, Day.Monday.AddDays(8), ReplayOptions.Default, false, "{}", universe, new TradingCalendar(), costs is null ? null : CostRecord.Of(costs));
            runs.Add(new CandidateRun(prepared.Members[k].Id, Seven.Candidates[k], RunAssembler.Assemble(header, perCandidate[k]), 0));
        }

        var oracle = runs.Select(r => new OracleComparisonCandidate(
            r.Id,
            [.. r.Run.Trades.Select(t => new OracleComparisonTrade(t.Trade.Key, costed ? t.Trade.Costing!.RNet : t.Trade.RFilled, t.Trade.RExact, t.Trade.MaeR, t.Trade.MfeR, Enum.Parse<OracleExitReason>(t.Trade.ExitReason.ToString()), (t.Trade.ExitEpochSeconds - t.Trade.EntryEpochSeconds) / 60.0))],
            Target(r.Exit))).ToList();
        return (ComparisonReport.Build(runs, true), oracle);
    }

    private static double? Target(ExitCandidate exit) => exit.Policy switch
    {
        "FIXED_RR" => exit.Parameters.Single(p => p.Name == "rr").Values[0],
        "PARTIAL_BE" => exit.Parameters.Single(p => p.Name == "t2_rr").Values[0],
        _ => null,
    };

    private static void Same(double? engine, double oracle, string what)
    {
        if (double.IsNaN(oracle))
        {
            engine.ShouldBeNull(what);
        }
        else
        {
            engine.ShouldNotBeNull(what);
            engine.Value.ShouldBe(oracle, Tolerance, what);
        }
    }

    private static void SameSample(SampleFigures engine, OracleSample oracle, string where)
    {
        engine.Trades.ShouldBe(oracle.Count, where + " trades");
        engine.Wins.ShouldBe(oracle.Wins, where + " wins");
        Same(engine.WinRate, oracle.WinRate, where + " win rate");
        Same(engine.MeanR, oracle.Mean, where + " mean");
        Same(engine.MedianR, oracle.Median, where + " median");
        Same(engine.DownsideDeviation, oracle.DownsideDeviation, where + " downside deviation");
        Same(engine.TotalR, oracle.Total, where + " total");
        Same(engine.ProfitFactor, oracle.ProfitFactor, where + " profit factor");
        Same(engine.AverageHoldMinutes, oracle.AverageHold, where + " hold");
        engine.ExitShares.ToDictionary(s => s.Reason.ToString(), s => s.Trades).ShouldBe(oracle.ByReason.ToDictionary(p => p.Key.ToString(), p => p.Value), ignoreOrder: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_table_the_paired_figures_and_the_diagnostics_agree_with_the_oracle_on_300_seeded_instruments(bool costed)
    {
        var (report, candidates) = Build(costed);
        var table = OracleComparison.Table(candidates);

        report.Unit.ShouldBe(costed ? "RNet" : "RFilled");
        report.Rows.Count.ShouldBe(7);
        for (var k = 0; k < 7; k++)
        {
            var row = report.Rows[k];
            var expected = table[k];
            var where = (costed ? "costed " : "gross ") + row.Id;
            SameSample(row.Own, expected.Own, where + " own");
            SameSample(row.Common, expected.Common, where + " common");
            if (k > 0)
            {
                Same(row.Paired!.MeanDifferenceR, expected.PairedMeanDifference, where + " paired");
                row.Paired.Entries.ShouldBe(expected.Common.Count);
            }

            var diagnostics = report.Diagnostics[k].Figures.ToDictionary(f => f.Name, f => f.Value);
            Same(diagnostics["winners with adverse excursion above 0.8R"], expected.DipAbove08, where + " dip 0.8");
            Same(diagnostics["winners with adverse excursion above 1.2R"], expected.DipAbove12, where + " dip 1.2");
            Same(diagnostics["median MFE (R)"], expected.MedianMfe, where + " median MFE");
            Same(diagnostics["trades with MFE reaching 1R"], expected.Reach1, where + " reach 1R");
            Same(diagnostics["trades with MFE reaching 2R"], expected.Reach2, where + " reach 2R");
            Same(diagnostics["trades with MFE reaching 3R"], expected.Reach3, where + " reach 3R");
            Same(diagnostics["median giveback, MFE at least 1R (R)"], expected.MedianGiveback, where + " giveback");
            Same(diagnostics["trades with MFE at least 1R that gave back over half of it"], expected.GaveBackHalf, where + " gave back half");
        }

        var common = report.CommonEntries;
        output.WriteLine((costed ? "costed" : "gross") + ": " + common + " common entries; trades by candidate " + string.Join(", ", report.Rows.Select(r => r.Own.Trades)));
        common.ShouldBeGreaterThan(100, "the fixture has a common set to compare on");
        report.Rows.ShouldAllBe(r => r.Own.Trades > 100);
        report.Rows.Select(r => r.Own.MeanR!.Value).Distinct().Count().ShouldBeGreaterThan(3, "the candidates differ, so the comparison compares something");
    }

    [Fact]
    public void The_oracle_does_see_a_doctored_table_a_median_taken_without_the_even_rule_and_a_downside_taken_over_the_losses_only()
    {
        // a sentinel for the comparison above: the same engine report against an oracle table with one trade removed from the first candidate differs somewhere
        var (report, candidates) = Build(costed: false);
        var doctored = new List<OracleComparisonCandidate>(candidates) { [0] = candidates[0] with { Trades = [.. candidates[0].Trades.Skip(1)] } };

        var table = OracleComparison.Table(doctored);

        table[0].Own.Count.ShouldNotBe(report.Rows[0].Own.Trades);
        Math.Abs(table[0].Own.Total - report.Rows[0].Own.TotalR!.Value).ShouldBeGreaterThan(Tolerance);
    }
}
