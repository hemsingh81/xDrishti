using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>BK-07 kernels worked by hand: the overlap, the median split, the ranking noise rule, the breadth and ADV reuse, the matrix and the drawdown series.</summary>
public sealed class BasketProfileKernelsTests
{
    private static readonly BasketProfileOptions Options = new();

    private static DateOnly D(int i) => BasketAnalyticsTests.D(i);

    private static HashSet<string> Set(params string[] items) => [.. items];

    [Fact]
    public void The_overlap_of_two_member_sets_is_the_jaccard_index_by_hand()
    {
        var shared = MemberOverlap.Jaccard(Set("1", "2", "3", "4"), Set("3", "4", "5"));
        var none = MemberOverlap.Jaccard(Set("1"), Set("2"));
        var empty = MemberOverlap.Jaccard(Set(), Set());

        shared.Number!.Value.ShouldBe(0.4, 1e-12);
        none.Number!.Value.ShouldBe(0.0, 1e-12);
        empty.State.ShouldBe(MetricState.NotEvaluable);
        empty.Number.ShouldBeNull();
    }

    private static IEnumerable<HoldOutTrade> Trades(string strategy, int firstDay, params double[] r) =>
        r.Select((x, i) => new HoldOutTrade(strategy, D(firstDay + i), x));

    [Fact]
    public void The_hold_out_splits_at_the_median_exit_date_and_compares_the_sign_of_each_half()
    {
        // 60 trades on 60 days: the split is day 30; the first 30 average +0.5, the second 30 average +0.2
        var agree = HoldOutStability.Compute(Trades("ORB", 0, [.. Enumerable.Repeat(0.5, 30), .. Enumerable.Repeat(0.2, 30)])).Single();
        var disagree = HoldOutStability.Compute(Trades("ORB", 0, [.. Enumerable.Repeat(0.5, 30), .. Enumerable.Repeat(-0.2, 30)])).Single();

        agree.Verdict.ShouldBe(HoldOutStability.Agrees);
        agree.SplitDate.ShouldBe(D(30));
        (agree.FirstN, agree.SecondN).ShouldBe((30, 30));
        agree.FirstExpectancy.Number!.Value.ShouldBe(0.5, 1e-12);
        agree.SecondExpectancy.Number!.Value.ShouldBe(0.2, 1e-12);
        disagree.Verdict.ShouldBe(HoldOutStability.Disagrees);
    }

    [Fact]
    public void A_half_under_thirty_trades_is_insufficient_with_the_n_of_each_half_and_no_number()
    {
        var cell = HoldOutStability.Compute(Trades("ORB", 0, [.. Enumerable.Repeat(1.0, 29), .. Enumerable.Repeat(1.0, 30)])).Single();

        cell.Verdict.ShouldBe(HoldOutStability.Insufficient);
        (cell.FirstN, cell.SecondN).ShouldBe((29, 30));
        cell.FirstExpectancy.State.ShouldBe(MetricState.Insufficient);
        cell.FirstExpectancy.Number.ShouldBeNull();
        cell.FirstExpectancy.Reason.ShouldBe("n = 29, at least 30 needed");
        cell.SecondExpectancy.Number.ShouldBe(1.0);
    }

    [Fact]
    public void An_expectancy_of_exactly_zero_has_no_sign_and_each_strategy_is_its_own_cell()
    {
        var cells = HoldOutStability.Compute([.. Trades("B", 0, [.. Enumerable.Repeat(0.0, 30), .. Enumerable.Repeat(1.0, 30)]), .. Trades("A", 0, [.. Enumerable.Repeat(1.0, 60)])]);

        cells.Select(c => c.Strategy).ShouldBe(["A", "B"]);
        cells[0].Verdict.ShouldBe(HoldOutStability.Agrees);
        cells[1].Verdict.ShouldBe(HoldOutStability.NotEvaluable);
    }

    private static IndexSeries Index(Func<int, double> returnOf, int days = 40)
    {
        var points = new List<IndexPoint> { new(D(0), D(0), 100, 0, 3) };
        for (var i = 1; i <= days; i++)
        {
            var r = returnOf(i);
            points.Add(new IndexPoint(D(i), D(i - 1), points[^1].Level * (1 + r), r, 3));
        }

        return new IndexSeries(Metric.Of(points[^1].Level), points, [], 0, days, days);
    }

    private static IReadOnlyList<RelativeStrengthReturn> Rs(double? value, int window = 21) =>
        [new(window, value is { } v ? Metric.Of(v) : Metric.Insufficient("n = 3, at least 22 needed"))];

    [Fact]
    public void A_clear_gap_ranks_the_baskets_and_is_separated()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var board = BasketRankBoard.Compute([new RankInput(a, Index(_ => 0.001), Rs(0.05)), new RankInput(b, Index(_ => 0.0), Rs(0.01)), new RankInput(c, Index(_ => -0.001), Rs(-0.03))]);

        board.RankWindow.ShouldBe(21);
        board.Rows.Select(r => r.Rank.Number).ShouldBe([1, 2, 3]);
        board.Separated.ShouldBeTrue();
    }

    [Fact]
    public void A_basket_with_an_insufficient_input_shows_the_state_and_no_rank()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var board = BasketRankBoard.Compute([new RankInput(a, Index(_ => 0.001), Rs(0.05)), new RankInput(b, Index(_ => 0.0), Rs(null)), new RankInput(c, Index(_ => -0.001), Rs(-0.03))]);

        board.Rows[1].Rank.State.ShouldBe(MetricState.Insufficient);
        board.Rows[1].Rank.Number.ShouldBeNull();
        board.Rows.Select(r => r.Rank.Number).ToArray().ShouldBe([1, null, 2]);
    }

    [Fact]
    public void Gaps_within_the_noise_of_the_pair_are_not_separated()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());

        // the daily difference swings by 4 points, so a gap of 0.01 is far inside its standard error
        var board = BasketRankBoard.Compute([new RankInput(a, Index(i => 0.01 + (i % 2 == 0 ? 0.02 : -0.02)), Rs(0.02)), new RankInput(b, Index(i => 0.0099 + (i % 2 == 0 ? -0.02 : 0.02)), Rs(0.01))]);

        board.Separated.ShouldBeFalse();
        board.Rows.Select(r => r.Rank.Number).ShouldBe([1, 2]);
    }

    [Fact]
    public void Fewer_than_two_values_give_no_rank_window_and_the_risk_adjusted_return_is_the_return_over_the_annual_vol()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());

        var board = BasketRankBoard.Compute([new RankInput(a, Index(i => i % 2 == 0 ? 0.02 : 0.0), Rs(0.05)), new RankInput(b, Index(_ => 0.0), Rs(null))]);

        board.RankWindow.ShouldBeNull();
        board.Separated.ShouldBeFalse();
        board.Rows[0].Rank.Number.ShouldBeNull();
        var returns = Enumerable.Range(1, 40).Select(i => i % 2 == 0 ? 0.02 : 0.0).ToArray();
        var mean = returns.Average();
        var sd = Math.Sqrt(returns.Sum(x => (x - mean) * (x - mean)) / 39);
        var total = returns.Aggregate(1.0, (acc, x) => acc * (1 + x)) - 1;
        board.Rows[0].RiskAdjusted.Number!.Value.ShouldBe(total / (sd * Math.Sqrt(252)), 1e-9);
    }

    [Fact]
    public void Breadth_uses_the_voter_rule_of_the_tracked_set_a_member_with_a_missing_session_does_not_vote()
    {
        MemberSeries Bars(string s, int skip, double last) => new(s, [.. Enumerable.Range(0, 20).Where(i => i != skip).Select(i => new DailyClose(D(i), i == 19 ? last : 100))], [], false, false);
        var members = new List<MemberSeries>();
        members.AddRange(Enumerable.Range(0, 5).Select(i => Bars($"U{i}", -1, 110)));
        members.AddRange(Enumerable.Range(0, 3).Select(i => Bars($"L{i}", -1, 90)));
        members.Add(Bars("G", 7, 110));

        var result = BasketBreadth.Compute(members, D(19), Options);

        // G has 19 bars: the calendar of the basket has 20 sessions and G lacks one, so it is left out of the 20-day figure and named
        result.AboveSma20.Voters.ShouldBe(8);
        result.AboveSma20.Excluded.ShouldBe(["G"]);
        result.AboveSma20.Share.Number!.Value.ShouldBe(0.625, 1e-9);
    }

    [Fact]
    public void The_breadth_series_gives_a_share_per_session_and_an_insufficient_day_has_no_number()
    {
        MemberSeries Bars(string s, double bump) => new(s, [.. Enumerable.Range(0, 30).Select(i => new DailyClose(D(i), i == 29 ? 100 + bump : 100))], [], false, false);
        var members = Enumerable.Range(0, 6).Select(i => Bars($"M{i}", i < 3 ? 10 : -10)).ToList();

        var series = BasketBreadth.SeriesAbove20(members, D(29), 5, Options);

        series.Select(p => p.Date).ShouldBe([D(25), D(26), D(27), D(28), D(29)]);
        series[^1].Share.Number!.Value.ShouldBe(0.5, 1e-9);
        series[0].Share.Number!.Value.ShouldBe(0.0, 1e-9);
        BasketBreadth.SeriesAbove20(members.Take(4).ToList(), D(29), 2, Options).ShouldAllBe(p => p.Share.Number == null && p.Share.State == MetricState.Insufficient);
    }

    [Fact]
    public void Adv20_is_the_cf05_median_of_the_traded_value_and_a_member_with_fewer_than_20_sessions_is_listed()
    {
        // traded values 1..20 (close 1, volume 1..20): the median of an even count is the mean of the two middle values, 10.5
        MemberSeries Member(string s, int days) => new(s, [.. Enumerable.Range(0, days).Select(i => new DailyClose(D(i), 1))], [.. Enumerable.Range(0, days).Select(i => new DailyVolume(D(i), i + 1))], false, false);

        var result = BasketCapacity.Compute([Member("F", 20), Member("T", 19)], D(19), Options);

        result.AdvBySymbol["F"].ShouldBe(10.5, 1e-12);
        result.ShortMembers.ShouldBe(["T"]);
        Domain.Research.LiquidityTiers.Median([.. Enumerable.Range(1, 20).Select(i => (decimal)i)]).ShouldBe(10.5m);
    }

    [Fact]
    public void The_matrix_is_symmetric_with_a_unit_diagonal_in_the_order_given_and_a_thin_pair_is_null()
    {
        var rng = new Random(7);
        IReadOnlyDictionary<string, IReadOnlyList<DailyReturn>> returns = new Dictionary<string, IReadOnlyList<DailyReturn>>
        {
            ["A"] = [.. Enumerable.Range(1, 50).Select(i => new DailyReturn(D(i), rng.NextDouble() - 0.5))],
            ["B"] = [.. Enumerable.Range(1, 50).Select(i => new DailyReturn(D(i), rng.NextDouble() - 0.5))],
            ["T"] = [.. Enumerable.Range(1, 10).Select(i => new DailyReturn(D(i), rng.NextDouble() - 0.5))],
        };

        var matrix = PairwiseCorrelation.Matrix(["B", "A", "T"], returns, D(50), Options);

        matrix.Symbols.ShouldBe(["B", "A", "T"]);
        matrix.Rho[0][0].ShouldBe(1.0);
        matrix.Rho[0][1].ShouldBe(matrix.Rho[1][0]);
        matrix.Rho[0][1].ShouldNotBeNull();
        matrix.Rho[0][2].ShouldBeNull();
        matrix.Rho[2][0].ShouldBeNull();
    }

    [Fact]
    public void The_drawdown_series_is_the_fall_from_the_running_peak()
    {
        var index = Index(i => i switch { 1 => 0.10, 2 => -0.10, 3 => 0.0, _ => 0.0 }, 4);

        var series = RiskMetrics.DrawdownSeries(index);

        series.ToArray().ShouldBe([0.0, 0.0, -0.10, -0.10, -0.10], 1e-12);
    }
}
