using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>BK-06: the basket index and the figures on it, worked by hand (acceptance criteria 1 to 11).</summary>
public sealed class BasketAnalyticsTests
{
    private static readonly BasketProfileOptions Options = new();

    internal static DateOnly D(int i) => new DateOnly(2026, 1, 1).AddDays(i);

    /// <summary>Closes from 100 that make the given daily returns.</summary>
    internal static MemberSeries FromReturns(string symbol, params double[] returns)
    {
        var closes = new List<DailyClose> { new(D(0), 100) };
        for (var i = 0; i < returns.Length; i++)
        {
            closes.Add(new DailyClose(D(i + 1), closes[^1].Close * (1 + returns[i])));
        }

        return new MemberSeries(symbol, closes, [], false, false);
    }

    internal static MemberSeries WithCloses(string symbol, params double[] closes) =>
        new(symbol, [.. closes.Select((c, i) => new DailyClose(D(i), c))], [], false, false);

    private static HashSet<string> All(params MemberSeries[] members) => members.Select(m => m.Symbol).ToHashSet(StringComparer.Ordinal);

    internal static IndexSeries Build(MemberSeries[] members, int lastDay, BasketProfileOptions? options = null) =>
        EqualWeightIndex.Build(members, _ => All(members), new ProfileWindow(D(0), D(lastDay)), options ?? Options);

    private static MemberSeries[] Three() => [FromReturns("A", 0.02, -0.01, 0.03), FromReturns("B", 0.04, 0.0, -0.02), FromReturns("C", -0.01, 0.02, 0.01)];

    internal static IndexSeries Manual(params double[] returns)
    {
        var points = new List<IndexPoint> { new(D(0), D(0), 100, 0, 0) };
        for (var i = 0; i < returns.Length; i++)
        {
            points.Add(new IndexPoint(D(i + 1), D(i), points[^1].Level * (1 + returns[i]), returns[i], 3));
        }

        return new IndexSeries(Metric.Of(points[^1].Level), points, [], 0, returns.Length, returns.Length);
    }

    private static IndexSeries FromLevels(params double[] levels) =>
        new(Metric.Of(levels[^1]), [.. levels.Select((l, i) => new IndexPoint(D(i), D(Math.Max(0, i - 1)), l, 0, 3))], [], 0, levels.Length - 1, levels.Length - 1);

    [Fact]
    public void The_index_is_the_daily_rebalanced_mean_and_permuting_members_changes_nothing()
    {
        var index = Build(Three(), 3);

        index.Status.State.ShouldBe(MetricState.Value);
        index.Points.Select(p => p.Level).ToArray().ShouldBe([100, 101.6667, 102.0056, 102.6856], 1e-4);
        index.Points.Skip(1).Select(p => p.Return).ToArray().ShouldBe([0.016667, 0.003333, 0.006667], 1e-4);
        var permuted = Build([.. Three().Reverse()], 3);
        permuted.Points.Select(p => p.Level).ShouldBe(index.Points.Select(p => p.Level));
    }

    [Fact]
    public void A_discontinuity_removes_that_instrument_day_only_and_is_named()
    {
        var c = new MemberSeries("C", [new(D(0), 198), new(D(1), 200), new(D(2), 100), new(D(3), 101)], [], false, false);
        var index = Build([FromReturns("A", 0.02, -0.01, 0.03), FromReturns("B", 0.04, 0.0, -0.02), c], 3, Options with { MinMembersForReturn = 2 });

        index.Points[1].Members.ShouldBe(3);
        index.Points[2].Members.ShouldBe(2);
        index.Points[2].Return.ShouldBe(-0.005, 1e-9);
        index.LeftOut.ShouldBe([new LeftOutDay("C", D(2), BuyAndHold.Discontinuity)]);
    }

    [Fact]
    public void A_member_that_joins_the_membership_counts_from_the_next_day_not_the_day_it_joined()
    {
        var members = new[] { FromReturns("A", 0.01, 0.01, 0.01, 0.01), FromReturns("B", 0.01, 0.01, 0.01, 0.01), FromReturns("C", 0.01, 0.01, 0.01, 0.01), FromReturns("D", 0.1, 0.1, 0.1, 0.1) };
        var index = EqualWeightIndex.Build(members, d => d >= D(3) ? All(members) : All(members[..3]), new ProfileWindow(D(0), D(4)), Options);

        index.Points.Select(p => p.Members).ToArray().ShouldBe([0, 3, 3, 3, 4]);
    }

    [Fact]
    public void An_index_that_lacks_members_on_most_days_is_insufficient_and_has_no_points()
    {
        var index = Build([FromReturns("A", 0.01, 0.01), FromReturns("B", 0.01, 0.01)], 2);

        index.Status.State.ShouldBe(MetricState.Empty);
        index.Points.ShouldBeEmpty();
    }

    [Fact]
    public void Volatility_is_the_annualised_sample_stdev_and_a_long_window_is_insufficient()
    {
        var index = Build(Three(), 3);

        var test = RiskMetrics.Volatility(index, [3]).Single();
        test.Volatility.Number!.Value.ShouldBe(0.11015, 1e-4);

        var thirty = RiskMetrics.Volatility(Manual([.. Enumerable.Range(0, 29).Select(i => i % 2 == 0 ? 0.01 : -0.01)]), [20, 60, 252]);
        thirty[0].Volatility.State.ShouldBe(MetricState.Value);
        thirty[1].Volatility.State.ShouldBe(MetricState.Insufficient);
        thirty[1].Volatility.Reason.ShouldBe("n = 30, at least 61 needed");
        thirty[2].Volatility.Reason.ShouldBe("n = 30, at least 253 needed");
    }

    [Fact]
    public void Drawdown_is_worked_by_hand_open_and_closed()
    {
        var open = RiskMetrics.Drawdown(FromLevels(100, 110, 99, 104.5), 4);
        open.MaxDrawdown.Number!.Value.ShouldBe(-0.10, 1e-9);
        open.Open.ShouldBeTrue();
        open.DaysSincePeak.ShouldBe(2);
        open.LongestDays.Number!.Value.ShouldBe(2);

        var closed = RiskMetrics.Drawdown(FromLevels(100, 110, 99, 121), 4);
        closed.Open.ShouldBeFalse();
        closed.LongestDays.Number!.Value.ShouldBe(2);

        RiskMetrics.Drawdown(FromLevels(100, 110, 99, 121), 20).MaxDrawdown.State.ShouldBe(MetricState.Insufficient);
    }

    [Fact]
    public void Beta_alpha_tracking_error_information_ratio_and_capture_are_worked_by_hand()
    {
        var index = Build(Three(), 3);
        var options = Options with { BetaWindows = [3], RollingBetaWindow = 3, MinCaptureDays = 2 };
        var benchmark = new[] { new DailyReturn(D(1), 0.01), new DailyReturn(D(2), -0.005), new DailyReturn(D(3), 0.01) };

        var relation = BenchmarkRegression.Compute(index, benchmark, options);

        var f = relation.Figures.Single();
        f.Beta.Number!.Value.ShouldBe(0.5556, 1e-3);
        f.Alpha.Number!.Value.ShouldBe(1.54, 1e-3);
        f.TrackingError.Number!.Value.ShouldBe(0.10017, 1e-3);
        f.InformationRatio.Number!.Value.ShouldBe(9.784, 1e-3);
        relation.Capture.Up.Number!.Value.ShouldBe(1.1667, 1e-3);
        relation.Capture.Down.State.ShouldBe(MetricState.Insufficient);
        relation.Capture.Down.Reason.ShouldBe("1 down days, at least 2 needed");
        relation.RollingBeta.Single().Beta.ShouldBe(0.5556, 1e-3);
    }

    [Fact]
    public void A_constant_index_gives_not_evaluable_never_infinity()
    {
        var index = Build(Three(), 3);
        var flat = new[] { new DailyReturn(D(1), 0.01), new DailyReturn(D(2), 0.01), new DailyReturn(D(3), 0.01) };

        var f = BenchmarkRegression.Compute(index, flat, Options with { BetaWindows = [3] }).Figures.Single();

        f.Beta.State.ShouldBe(MetricState.NotEvaluable);
        f.Alpha.State.ShouldBe(MetricState.NotEvaluable);
        f.InformationRatio.State.ShouldBe(MetricState.NotEvaluable);
    }

    [Fact]
    public void Relative_strength_is_the_ratio_line_and_the_rank_shares_ties()
    {
        var index = Manual(0.10, 0.10);
        var bench = new[] { new DailyClose(D(0), 1000), new DailyClose(D(1), 1000), new DailyClose(D(2), 1100) };

        var rs = RelativeStrength.Compute(index, bench, [2, 3]);

        rs.Line.Select(p => p.Value).ToArray().ShouldBe([100, 110, 110], 1e-9);
        rs.Returns[0].Return.Number!.Value.ShouldBe(0.10, 1e-9);
        rs.Returns[1].Return.State.ShouldBe(MetricState.Insufficient);
        RelativeStrength.Rank(Metric.Of(5), [Metric.Of(5), Metric.Of(5), Metric.Of(9)]).Number.ShouldBe(2);
        RelativeStrength.Rank(Metric.Of(5), [Metric.Of(5)]).State.ShouldBe(MetricState.Insufficient);
    }

    [Fact]
    public void Contribution_ties_to_the_hold_and_sums_to_one()
    {
        MemberSeries Two(string s, double a, double b) => new(s, [new(D(0), a), new(D(80), b)], [], false, false);
        var members = new[] { Two("A", 100, 110), Two("B", 50, 45), Two("C", 200, 250) };

        var result = Contribution.Compute(members, new ProfileWindow(D(0), D(80)), Options);

        result.ReturnPct.Number!.Value.ShouldBe(8.3333, 1e-3);
        result.Rows.Single(r => r.Symbol == "A").Share.ShouldBe(0.4, 1e-9);
        result.Rows.Single(r => r.Symbol == "B").Share.ShouldBe(-0.4, 1e-9);
        result.Rows.Single(r => r.Symbol == "C").Share.ShouldBe(1.0, 1e-9);
        result.Rows.Sum(r => r.Share).ShouldBe(1.0, 1e-9);
        result.Top3Share.Number!.Value.ShouldBe(1.0, 1e-9);

        var zero = Contribution.Compute([Two("A", 100, 110), Two("B", 100, 90), Two("C", 100, 100)], new ProfileWindow(D(0), D(80)), Options);
        zero.Top3Share.State.ShouldBe(MetricState.NotEvaluable);
        zero.Rows.ShouldBeEmpty();

        var constant = Contribution.Compute([Two("A", 100, 110), Two("B", 100, 90), Two("C", 100, 100), Two("K", 50, 50), Two("L", 20, 24)], new ProfileWindow(D(0), D(80)), Options);
        constant.Rows.Single(r => r.Symbol == "K").ReturnPct.ShouldBe(0.0);
    }

    [Fact]
    public void Correlation_is_the_mean_of_the_pairs_and_the_effective_bets_follow()
    {
        var three = Options with { MinCorrelationMembers = 3, MinCorrelationPairs = 3 };

        var r = PairwiseCorrelation.Summarise(3, [0.6, 0.3, 0.0], three, false);

        r.AveragePairwise.Number!.Value.ShouldBe(0.3, 1e-9);
        r.EffectiveBets.Number!.Value.ShouldBe(2.4, 1e-9);
        var four = PairwiseCorrelation.Summarise(4, [0.6, 0.3, 0.0, 0.1, 0.1, 0.1], Options, false);
        four.AveragePairwise.State.ShouldBe(MetricState.Insufficient);
        four.AveragePairwise.Reason.ShouldBe("n = 4, at least 5 needed");
        PairwiseCorrelation.Summarise(3, [1.0, 1.0, 1.0], three, false).EffectiveBets.Number.ShouldBe(1.0);
    }

    [Fact]
    public void The_pair_correlation_is_the_one_the_gate_and_the_fill_use()
    {
        var rng = new Random(7);
        var p = Enumerable.Range(0, 60).Select(i => new DailyReturn(D(i), (rng.NextDouble() - 0.5) / 10)).ToList();
        var q = p.Select(r => new DailyReturn(r.Date, (2 * r.Value) + ((rng.NextDouble() - 0.5) / 50))).ToList();
        var perfect = p.Select(r => new DailyReturn(r.Date, 3 * r.Value)).ToList();
        var near = p.Select(r => new DailyReturn(r.Date, (3 * r.Value) + ((rng.NextDouble() - 0.5) / 1000))).ToList();
        var returns = new Dictionary<string, IReadOnlyList<DailyReturn>> { ["P"] = p, ["Q"] = q, ["R"] = near };

        var table = CorrelationTable.FromReturns(returns, D(60), ["P", "Q", "R"], null, 60, 40);
        var result = PairwiseCorrelation.Compute(returns, new Dictionary<string, double>(), D(59), Options with { MinCorrelationMembers = 3, MinCorrelationPairs = 3 });

        PairwiseCorrelation.Pair([.. p.Select(r => r.Value)], [.. perfect.Select(r => r.Value)])!.Value.ShouldBe(1.0, 1e-9);
        table.Get("P", "R")!.Value.ShouldBeGreaterThan(0.99);
        var mean = new[] { table.Get("P", "Q")!.Value, table.Get("P", "R")!.Value, table.Get("Q", "R")!.Value }.Average();
        result.AveragePairwise.Number!.Value.ShouldBe(mean, 1e-12);
        result.Pairs.ShouldBe(3);
    }

    [Fact]
    public void Breadth_counts_voters_names_the_short_history_and_needs_the_minimum()
    {
        MemberSeries Bars(string s, int n, double last) => new(s, [.. Enumerable.Range(0, n).Select(i => new DailyClose(D(20 - n + i), i == n - 1 ? last : 100))], [], false, false);
        var members = new List<MemberSeries>();
        members.AddRange(Enumerable.Range(0, 5).Select(i => Bars($"U{i}", 20, 110)));
        members.AddRange(Enumerable.Range(0, 3).Select(i => Bars($"L{i}", 20, 90)));
        members.AddRange(Enumerable.Range(0, 2).Select(i => Bars($"S{i}", 10, 110)));

        var result = BasketBreadth.Compute(members, D(19), Options);

        result.AboveSma20.Share.Number!.Value.ShouldBe(0.625, 1e-9);
        result.AboveSma20.Voters.ShouldBe(8);
        result.AboveSma20.Excluded.ShouldBe(["S0", "S1"]);
        result.AboveSma20.ExcludedReason.ShouldBe(BasketBreadth.ShortHistory);
        result.AboveSma50.Share.State.ShouldBe(MetricState.Insufficient);
        BasketBreadth.AdvanceDecline(30, 20, 5, 5).Share.Number!.Value.ShouldBe(0.6, 1e-9);
        BasketBreadth.Share(2, 4, 5, []).Share.Reason.ShouldBe("n = 4, at least 5 needed");
    }

    [Fact]
    public void The_new_high_share_needs_252_sessions_and_leaves_shorter_members_out_of_that_figure_only()
    {
        MemberSeries Bars(string s, int n, bool atHigh) => new(s, [.. Enumerable.Range(0, n).Select(i => new DailyClose(D(252 - n + i), i == n - 1 ? (atHigh ? 500 : 100) : 200))], [], false, false);
        var members = new List<MemberSeries> { Bars("H0", 252, true), Bars("H1", 252, true) };
        members.AddRange(Enumerable.Range(0, 6).Select(i => Bars($"N{i}", 252, false)));
        members.Add(Bars("S", 100, true));

        var result = BasketBreadth.Compute(members, D(251), Options);

        result.NewHighShare.Share.Number!.Value.ShouldBe(0.25, 1e-9);
        result.NewHighShare.Excluded.ShouldBe(["S"]);
        result.NoBar.ShouldBeEmpty();
    }

    [Fact]
    public void Capacity_is_worked_by_hand_and_a_member_with_a_short_window_is_listed()
    {
        var result = BasketCapacity.FromAdv([("A", 50, true, false), ("B", 20, false, true), ("C", 10, true, false)], ["Z"], 0.01);

        result.MedianAdv.Number!.Value.ShouldBe(20, 1e-9);
        result.P10Adv.Number!.Value.ShouldBe(12, 1e-9);
        result.BasketCapacity.Number!.Value.ShouldBe(0.8, 1e-9);
        result.FnoShare.Number!.Value.ShouldBe(2.0 / 3, 1e-9);
        result.ShortMembers.ShouldBe(["Z"]);

        var vol = Enumerable.Range(0, 20).Select(i => new DailyVolume(D(i), 10)).ToList();
        var full = new MemberSeries("F", [.. Enumerable.Range(0, 20).Select(i => new DailyClose(D(i), 5))], vol, false, false);
        var thin = new MemberSeries("T", full.Closes, vol[..10], false, false);
        var computed = BasketCapacity.Compute([full, thin], D(19), Options);
        computed.AdvBySymbol["F"].ShouldBe(50);
        computed.ShortMembers.ShouldBe(["T"]);
    }

    [Fact]
    public void Turnover_is_worked_by_hand_and_one_snapshot_is_unavailable_with_its_date()
    {
        MemberTurnover.FromCounts(2, 1, 10).Number!.Value.ShouldBe(0.30, 1e-9);

        var single = MemberTurnover.Compute([D(5)], _ => new HashSet<string> { "A" }, new ProfileWindow(D(0), D(30)));
        single.Turnover.State.ShouldBe(MetricState.Unavailable);
        single.Turnover.Reason.ShouldBe("membership history starts on 2026-01-06");
        single.Basis.ShouldBe(MemberTurnover.SingleSnapshot);

        IReadOnlySet<string> Set(DateOnly d) => d < D(10) ? new HashSet<string> { "A", "B", "C" } : new HashSet<string> { "A", "B", "D" };
        var dated = MemberTurnover.Compute([D(0), D(10)], Set, new ProfileWindow(D(0), D(30)));
        dated.Adds.ShouldBe(1);
        dated.Removes.ShouldBe(1);
        dated.Turnover.Number!.Value.ShouldBe(2.0 / 3, 1e-9);
        dated.Basis.ShouldBe(MemberTurnover.Dated);
    }

    [Fact]
    public void Regimes_with_too_few_days_are_shown_as_insufficient_and_unknown_days_sit_in_no_group()
    {
        var index = Manual([.. Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? 0.01 : -0.004)]);
        var regimes = new Dictionary<DateOnly, int?>();
        for (var i = 0; i < 60; i++)
        {
            regimes[D(i)] = i < 45 ? 5 : i < 57 ? 7 : null;
        }

        var result = RegimeSplit.Compute(index, [], regimes, Options);

        result.Rows.Select(r => (r.Code, r.Days)).ToArray().ShouldBe([(5, 45), (7, 12)]);
        result.Rows[0].MeanDailyReturn.State.ShouldBe(MetricState.Value);
        result.Rows[0].HitRate.State.ShouldBe(MetricState.Unavailable);
        result.Rows[1].MeanDailyReturn.State.ShouldBe(MetricState.Insufficient);
        result.Rows[1].MeanDailyReturn.Reason.ShouldBe("n = 12, at least 30 needed");
        result.Unknown.ShouldBe(3);
        RegimeSplit.Compute(index, [], new Dictionary<DateOnly, int?>(), Options).Status.State.ShouldBe(MetricState.Unavailable);
    }
}
