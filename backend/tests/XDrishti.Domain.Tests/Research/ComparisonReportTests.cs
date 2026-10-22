using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>
/// Criteria 5 and 6 of P4-05: the comparison table and the MAE/MFE diagnostics equal a table worked out by hand before the code (gross and costed), with the common set, the paired difference and the censor levels. The fixture is six entries
/// on six days; candidate A (<c>FIXED_RR{rr=2}</c>) takes all six, candidate B (<c>FIXED_RR{rr=3}</c>) takes the first five. Per trade (day: R filled, R exact, MAE, MFE, reason, minutes held):
/// <code>
/// A  d1: +2.0  +2.0 0.5 2.0 Target 60     B  d1: +3.0 +3.0 0.5 3.0 Target 120
///    d2: -1.0  -1.0 1.0 0.4 Stop   30        d2: -1.0 -1.0 1.0 0.4 Stop    30
///    d3: +2.0  +2.0 0.9 2.0 Target 90        d3: +1.0 +1.0 1.3 2.2 SqOff  300
///    d4: -1.0  -1.0 1.0 1.2 Stop   20        d4: -1.0 -1.0 1.0 1.2 Stop    20
///    d5: +0.5  +0.5 0.3 0.8 SqOff 300        d5:  0.0  0.0 0.4 0.9 SqOff  300
///    d6: -0.5  -0.5 0.6 0.2 SqOff 200
/// </code>
/// </summary>
public sealed class ComparisonReportTests
{
    private const double Tolerance = 1e-9;

    private static readonly DateOnly Monday = new(2026, 3, 2);

    private static readonly CostRecord Sheet = new("test-a", new string('a', 64), "{}", null, 100_000m, SizingProfile.Standard, 1m);

    private sealed record T(int Day, double RFilled, double RExact, double Mae, double Mfe, ExitReason Reason, int Minutes);

    private static readonly T[] A =
    [
        new(1, 2.0, 2.0, 0.5, 2.0, ExitReason.Target, 60),
        new(2, -1.0, -1.0, 1.0, 0.4, ExitReason.Stop, 30),
        new(3, 2.0, 2.0, 0.9, 2.0, ExitReason.Target, 90),
        new(4, -1.0, -1.0, 1.0, 1.2, ExitReason.Stop, 20),
        new(5, 0.5, 0.5, 0.3, 0.8, ExitReason.SquareOff, 300),
        new(6, -0.5, -0.5, 0.6, 0.2, ExitReason.SquareOff, 200),
    ];

    private static readonly T[] B =
    [
        new(1, 3.0, 3.0, 0.5, 3.0, ExitReason.Target, 120),
        new(2, -1.0, -1.0, 1.0, 0.4, ExitReason.Stop, 30),
        new(3, 1.0, 1.0, 1.3, 2.2, ExitReason.SquareOff, 300),
        new(4, -1.0, -1.0, 1.0, 1.2, ExitReason.Stop, 20),
        new(5, 0.0, 0.0, 0.4, 0.9, ExitReason.SquareOff, 300),
    ];

    private static SimTrade Trade(T t, bool costed)
    {
        var date = Monday.AddDays(t.Day - 1);
        var entry = new DateTimeOffset(date.Year, date.Month, date.Day, 9, 45, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();
        var costing = costed
            ? new TradeCosting(CostSegment.IntradayEquity, Sheet.SheetId, 100, 1000m, 10000m, 10000m, LegCharges.Zero, LegCharges.Zero, 100m, 0.1, t.RFilled - 0.1)
            : null;
        return new SimTrade(
            SimTrade.CurrentSchemaVersion, "ORB", Guid.Empty, "X", Side.Buy, date, entry, Timeframe.M15, new Bar(entry - 900, 99, 100.5, 98, 100, 1_000), 15, true,
            entry, 100.5, 98, 105.5, entry + (t.Minutes * 60L), 105.5, t.Reason, t.RExact, t.Mae, t.Mfe, FillBasis.Exact, costed ? CostBasis.Charged : CostBasis.None, null,
            t.RFilled, 0, 100.5, 105.5, EntryFill.AtLimit, false, 1, 0, costing);
    }

    private static CandidateRun Candidate(string id, string rr, T[] trades, bool costed, long gated = 0)
    {
        var strategy = new StrategyRecord("ORB", new Dictionary<string, double> { ["or_min"] = 15 }, "{}", new string('1', 64), Timeframe.M15, [Side.Buy]);
        var universe = new UniverseRecord(UniverseKind.InstrumentList, null, null, UniverseLabels.InstrumentsAsListed, [new UniverseMember(Guid.Empty, "X")]);
        var header = new RunHeader(strategy, "sim-6", Monday, Monday.AddDays(6), ReplayOptions.Default, false, "{}", universe, new TradingCalendar([]), costed ? Sheet : null);
        var record = new InstrumentRun(Guid.Empty, "X", null, null, Monday, Monday, Monday, new DateSpan(Monday, Monday), 0, 0, 0, 0, null, 0.05m, false, [], SessionCounts.Empty, [], false, [], [], WalkCounters.Empty, RunFates.Empty, 0, 0);
        var run = RunAssembler.Assemble(header, [new InstrumentReplay(record, [.. trades.Select(t => Trade(t, costed))], [])]);
        return new CandidateRun(id, new ExitCandidate("FIXED_RR", [new ExitParameterValue("rr", [double.Parse(rr, System.Globalization.CultureInfo.InvariantCulture)])]), run, gated);
    }

    private static ComparisonReport Report(bool costed) => ComparisonReport.Build([Candidate("FIXED_RR{rr=2}", "2", A, costed, 1), Candidate("FIXED_RR{rr=3}", "3", B, costed)], gateApplied: true);

    [Fact]
    public void The_gross_table_equals_the_hand_computed_one_on_own_trades_and_on_the_common_set()
    {
        var report = Report(costed: false);
        var a = report.Rows[0];
        var b = report.Rows[1];

        report.Unit.ShouldBe("RFilled");
        report.CommonEntries.ShouldBe(5);
        (a.Own.Trades, a.Own.Wins).ShouldBe((6, 3));
        a.Own.WinRate.ShouldBe(0.5, Tolerance);
        a.Own.MeanR.ShouldBe(2.0 / 6, Tolerance);
        a.Own.MedianR.ShouldBe(0.0, Tolerance);
        a.Own.DownsideDeviation.ShouldBe(Math.Sqrt(2.25 / 6), Tolerance);
        a.Own.TotalR.ShouldBe(2.0, Tolerance);
        a.Own.ProfitFactor.ShouldBe(4.5 / 2.5, Tolerance);
        a.Own.AverageHoldMinutes.ShouldBe(700.0 / 6, Tolerance);
        a.Own.MeanCostR.ShouldBeNull();
        a.Own.ExitShares.Select(s => (s.Reason, s.Trades)).ShouldBe([(ExitReason.Stop, 2), (ExitReason.Target, 2), (ExitReason.SquareOff, 2)], ignoreOrder: true);

        (a.Common.Trades, a.Common.Wins).ShouldBe((5, 3));
        a.Common.WinRate.ShouldBe(0.6, Tolerance);
        a.Common.MeanR.ShouldBe(0.5, Tolerance);
        a.Common.MedianR.ShouldBe(0.5, Tolerance);
        a.Common.DownsideDeviation.ShouldBe(Math.Sqrt(0.4), Tolerance);
        a.Common.TotalR.ShouldBe(2.5, Tolerance);
        a.Common.ProfitFactor.ShouldBe(2.25, Tolerance);
        a.Paired.ShouldBeNull();
        a.NotCommon.ShouldBe(1);
        a.GatedBlocked.ShouldBe(1);

        (b.Own.Trades, b.Own.Wins).ShouldBe((5, 2));
        b.Own.WinRate.ShouldBe(0.4, Tolerance);
        b.Own.MeanR.ShouldBe(0.4, Tolerance);
        b.Own.MedianR.ShouldBe(0.0, Tolerance);
        b.Own.DownsideDeviation.ShouldBe(Math.Sqrt(0.4), Tolerance);
        b.Own.TotalR.ShouldBe(2.0, Tolerance);
        b.Own.ProfitFactor.ShouldBe(2.0, Tolerance);
        b.Own.AverageHoldMinutes.ShouldBe(770.0 / 5, Tolerance);
        b.Common.ShouldBe(b.Own);
        b.NotCommon.ShouldBe(0);
        b.Paired!.Entries.ShouldBe(5);
        b.Paired.MeanDifferenceR!.Value.ShouldBe(-0.1, Tolerance);
    }

    [Fact]
    public void The_costed_table_is_in_net_R_and_has_the_mean_cost()
    {
        var report = Report(costed: true);
        var a = report.Rows[0];

        report.Unit.ShouldBe("RNet");
        a.Own.MeanR.ShouldBe(1.4 / 6, Tolerance);
        a.Own.TotalR.ShouldBe(1.4, Tolerance);
        a.Own.MeanCostR.ShouldBe(0.1, Tolerance);
        a.Own.Wins.ShouldBe(3);
        a.Own.DownsideDeviation.ShouldBe(Math.Sqrt((1.21 + 1.21 + 0.36) / 6), Tolerance);
        report.Rows[1].Paired!.MeanDifferenceR!.Value.ShouldBe(-0.1, Tolerance);
    }

    [Fact]
    public void The_diagnostics_equal_the_hand_computed_figures_and_carry_the_censor_levels()
    {
        var a = Report(costed: false).Diagnostics[0];
        DiagnosticFigure Figure(string name) => a.Figures.Single(f => f.Name == name);

        a.Trades.ShouldBe(6);
        a.BelowMinimum.ShouldBeTrue();
        var dip08 = Figure("winners with adverse excursion above 0.8R");
        (dip08.Count, dip08.Of).ShouldBe((1, 3));
        dip08.Value!.Value.ShouldBe(1.0 / 3, Tolerance);
        dip08.Censor.ShouldBe("1R stop");
        dip08.Censored.ShouldBeFalse();
        var dip12 = Figure("winners with adverse excursion above 1.2R");
        (dip12.Count, dip12.Of).ShouldBe((0, 3));
        dip12.Censored.ShouldBeTrue("the stop at 1R ends a trade at 1R of adverse excursion");
        Figure("median MFE (R)").Value!.Value.ShouldBe(1.0, Tolerance);
        Figure("median MFE (R)").Censor.ShouldBe("2R target");
        Figure("trades with MFE reaching 1R").Value!.Value.ShouldBe(0.5, Tolerance);
        Figure("trades with MFE reaching 2R").Value!.Value.ShouldBe(2.0 / 6, Tolerance);
        var reach3 = Figure("trades with MFE reaching 3R");
        reach3.Count.ShouldBe(0);
        reach3.Censored.ShouldBeTrue("a target at 2R: no trade shows an MFE above it");
        var giveback = Figure("median giveback, MFE at least 1R (R)");
        (giveback.Count, giveback.Value!.Value).ShouldBe((3, 0.0));
        giveback.Censor.ShouldBe("none");
        var half = Figure("trades with MFE at least 1R that gave back over half of it");
        (half.Count, half.Of).ShouldBe((1, 3));
    }

    [Fact]
    public void A_cell_below_30_trades_has_no_reading_sentence_and_a_cell_of_30_has_one_except_where_censored()
    {
        var small = Report(costed: false).Diagnostics[0];
        var many = Enumerable.Range(0, 35).Select(i => new T(1 + (i % 6), 1.0, 1.0, 0.9, 1.5, ExitReason.Target, 30)).ToArray();
        var big = ComparisonReport.Build([Candidate("FIXED_RR{rr=2}", "2", [.. many.Select((t, i) => t with { Day = i + 1 })], costed: false)], gateApplied: true).Diagnostics[0];

        small.Figures.ShouldAllBe(f => f.Reading == null);
        big.BelowMinimum.ShouldBeFalse();
        big.Figures.Single(f => f.Name == "winners with adverse excursion above 0.8R").Reading.ShouldBe("winners with adverse excursion above 0.8R: 35 of 35");
        big.Figures.Single(f => f.Name == "winners with adverse excursion above 1.2R").Reading.ShouldBeNull("beyond the censor level of the stop");
        big.Figures.Single(f => f.Name == "trades with MFE reaching 3R").Reading.ShouldBeNull("beyond the censor level of the target");
    }

    [Fact]
    public void The_common_set_is_the_intersection_whatever_the_order_of_the_candidates()
    {
        var ab = ComparisonReport.Build([Candidate("A", "2", A, false), Candidate("B", "3", B, false)], true);
        var ba = ComparisonReport.Build([Candidate("B", "3", B, false), Candidate("A", "2", A, false)], true);

        ab.CommonEntries.ShouldBe(5);
        ba.CommonEntries.ShouldBe(5);
        ab.Rows[0].Common.ShouldBe(ba.Rows[1].Common);
        ab.Rows[1].Common.ShouldBe(ba.Rows[0].Common);
        ba.Rows[1].Paired!.MeanDifferenceR!.Value.ShouldBe(0.1, Tolerance);
    }

    [Fact]
    public void A_candidate_with_no_trade_has_a_row_of_nulls_and_empties_the_common_set()
    {
        var report = ComparisonReport.Build([Candidate("A", "2", A, false), Candidate("B", "3", [], false)], true);

        report.CommonEntries.ShouldBe(0);
        report.Rows[1].Own.Trades.ShouldBe(0);
        report.Rows[1].Own.MeanR.ShouldBeNull();
        report.Rows[1].Paired.ShouldBe(new PairedFigure(0, null));
        report.Rows[0].NotCommon.ShouldBe(6);
    }
}
