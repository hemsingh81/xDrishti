using XDrishti.Domain.Market.Strength;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Market.Strength;

/// <summary>BK-09 acceptance criteria 1 to 5: the three kernels by hand.</summary>
public sealed class StrengthKernelTests
{
    private static readonly DateOnly Day0 = new(2026, 1, 1);

    private static DateOnly D(int i) => Day0.AddDays(i);

    private static MemberSeries Series(string symbol, params double[] closes) =>
        new(symbol, [.. closes.Select((c, i) => new DailyClose(D(i), c))], [], false, false);

    private static double Score(RankResult r, string symbol) => r.Rows.Single(x => x.Symbol == symbol).Score.Number!.Value;

    private static double Pct(RankResult r, string symbol) => r.Rows.Single(x => x.Symbol == symbol).PctRank.Number!.Value;

    [Fact]
    public void Five_members_rank_by_score_with_the_percent_rank_of_the_spec()
    {
        MomentumInput[] inputs = [new("A", 0.30, 0.015), new("B", 0.20, 0.008), new("C", 0.10, 0.0125), new("D", -0.10, 0.010), new("E", 0.0, 0.010)];

        var result = RelativeStrengthRank.Rank(inputs, new StrengthOptions { MinVoters = 5 });

        Score(result, "A").ShouldBe(20, 1e-9);
        Score(result, "B").ShouldBe(25, 1e-9);
        Score(result, "C").ShouldBe(8, 1e-9);
        Score(result, "D").ShouldBe(-10, 1e-9);
        Score(result, "E").ShouldBe(0, 1e-9);
        Pct(result, "B").ShouldBe(1.0);
        Pct(result, "A").ShouldBe(0.75);
        Pct(result, "C").ShouldBe(0.50);
        Pct(result, "E").ShouldBe(0.25);
        Pct(result, "D").ShouldBe(0.0);
    }

    [Fact]
    public void The_production_floor_gives_insufficient_for_five_and_for_nine_but_a_rank_for_ten()
    {
        MomentumInput[] Make(int n) => [.. Enumerable.Range(0, n).Select(i => new MomentumInput("S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), 0.01 * (i + 1), 0.01))];

        RelativeStrengthRank.Rank(Make(5), new StrengthOptions()).Rows.ShouldAllBe(r => r.PctRank.State == MetricState.Insufficient);
        RelativeStrengthRank.Rank(Make(9), new StrengthOptions()).Rows.ShouldAllBe(r => r.PctRank.State == MetricState.Insufficient);
        var ten = RelativeStrengthRank.Rank(Make(10), new StrengthOptions());
        ten.Rows.ShouldAllBe(r => r.PctRank.State == MetricState.Value);
        Pct(ten, "S09").ShouldBe(1.0);
        Pct(ten, "S00").ShouldBe(0.0);
    }

    [Fact]
    public void Twelve_members_by_hand_and_a_new_lowest_member_lifts_every_other_rank_predictably()
    {
        var twelve = Enumerable.Range(0, 12).Select(i => new MomentumInput("S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), i + 1, 1)).ToList();
        var before = RelativeStrengthRank.Rank(twelve, new StrengthOptions());
        var after = RelativeStrengthRank.Rank([.. twelve, new MomentumInput("Z", -5, 1)], new StrengthOptions());

        Pct(before, "S05").ShouldBe(5.0 / 11, 1e-12);
        Pct(after, "S05").ShouldBe(6.0 / 12, 1e-12);
        Pct(after, "Z").ShouldBe(0.0);
    }

    [Fact]
    public void Equal_scores_share_the_average_rank_and_the_input_order_changes_nothing()
    {
        MomentumInput[] inputs = [new("A", 1, 1), new("B", 2, 1), new("C", 2, 1), new("D", 3, 1)];
        var options = new StrengthOptions { MinVoters = 4 };

        var forward = RelativeStrengthRank.Rank(inputs, options);
        var swapped = RelativeStrengthRank.Rank([inputs[0], inputs[2], inputs[1], inputs[3]], options);

        Pct(forward, "B").ShouldBe(0.5);
        Pct(forward, "C").ShouldBe(0.5);
        swapped.Rows.Select(r => (r.Symbol, r.PctRank.Number)).ShouldBe(forward.Rows.Select(r => (r.Symbol, r.PctRank.Number)));
    }

    [Fact]
    public void A_zero_volatility_member_is_not_evaluable_excluded_and_counted()
    {
        var flat = Series("F", Enumerable.Repeat(100.0, 10).ToArray());
        var (value, why) = RelativeStrengthRank.Momentum(flat, D(9), 5, 1);
        value!.Volatility.ShouldBe(0.0);

        var result = RelativeStrengthRank.Rank([value, new MomentumInput("A", 1, 1), new MomentumInput("B", 2, 1)], new StrengthOptions { MinVoters = 2 });

        why.ShouldBeNull();
        result.Rows.Single(r => r.Symbol == "F").Score.State.ShouldBe(MetricState.NotEvaluable);
        result.Rows.Single(r => r.Symbol == "F").Score.Number.ShouldBeNull();
        result.Ranked.ShouldBe(2);
        result.NotRanked.ShouldBe(1);
    }

    [Fact]
    public void The_latest_skip_sessions_never_enter_the_return_or_the_volatility()
    {
        // L = 4, s = 1, t = 5: R = c(4) / c(1) - 1; the close of session 5 is the skipped one
        var calm = Series("A", 100, 102, 104, 106, 108, 110);
        var spiked = Series("A", 100, 102, 104, 106, 108, 999);

        var (a, _) = RelativeStrengthRank.Momentum(calm, D(5), 4, 1);
        var (b, _) = RelativeStrengthRank.Momentum(spiked, D(5), 4, 1);

        a!.Return.ShouldBe((108.0 / 102) - 1, 1e-12);
        a.Volatility.ShouldBe(b!.Volatility, 1e-12);
        a.Return.ShouldBe(b.Return, 1e-12);
    }

    [Fact]
    public void An_ex_date_inside_the_window_is_not_evaluable_with_the_corporate_action_reason_and_one_before_it_is_not()
    {
        var closes = Series("A", 100, 102, 104, 106, 108, 110);

        var (inside, insideWhy) = RelativeStrengthRank.Momentum(closes, D(5), 4, 1, [D(3)]);
        var (before, _) = RelativeStrengthRank.Momentum(closes, D(5), 4, 1, [D(1)]);
        var (after, _) = RelativeStrengthRank.Momentum(closes, D(4), 3, 1, [D(5)]);

        inside.ShouldBeNull();
        insideWhy!.State.ShouldBe(MetricState.NotEvaluable);
        insideWhy.Reason.ShouldBe(RelativeStrengthRank.CorporateActionUnresolved);
        before.ShouldNotBeNull("the ex-date is on the first close of the window: that close is already ex");
        after.ShouldNotBeNull("an ex-date after the day is not read");
    }

    [Fact]
    public void A_short_window_or_a_missing_day_has_no_momentum_with_the_reason()
    {
        var (shortValue, shortWhy) = RelativeStrengthRank.Momentum(Series("A", 1, 2, 3), D(2), 4, 1);
        var (noDay, noDayWhy) = RelativeStrengthRank.Momentum(Series("A", 1, 2, 3, 4, 5, 6), D(9), 4, 1);

        shortValue.ShouldBeNull();
        shortWhy!.State.ShouldBe(MetricState.Unavailable);
        noDay.ShouldBeNull();
        noDayWhy!.Reason!.ShouldContain("no close");
    }

    [Fact]
    public void Index_relative_strength_is_the_difference_in_percentage_points()
    {
        var share = Series("S", Enumerable.Range(0, 21).Select(i => i == 0 ? 105.0 : i == 20 ? 110.0 : 107.0).ToArray());
        var index = Series("N", Enumerable.Range(0, 21).Select(i => i == 0 ? 22000.0 : i == 20 ? 22440.0 : 22100.0).ToArray());

        var metric = IndexRelative.Compute(share.Closes, index.Closes, D(20), 20);

        metric.State.ShouldBe(MetricState.Value);
        metric.Number!.Value.ShouldBe(2.7619, 1e-3);
    }

    [Fact]
    public void Index_relative_strength_without_enough_closes_or_index_bars_is_unavailable_never_zero()
    {
        var share = Series("S", Enumerable.Repeat(100.0, 20).ToArray());
        var index = Series("N", Enumerable.Repeat(100.0, 20).ToArray());

        var tooShort = IndexRelative.Compute(share.Closes, index.Closes, D(19), 20);
        var noIndex = IndexRelative.Compute(share.Closes, [], D(19), 20);
        var shareGap = IndexRelative.Compute(Series("S", Enumerable.Repeat(100.0, 5).ToArray()).Closes, Series("N", Enumerable.Repeat(100.0, 21).ToArray()).Closes, D(20), 20);

        tooShort.State.ShouldBe(MetricState.Unavailable);
        tooShort.Number.ShouldBeNull();
        noIndex.State.ShouldBe(MetricState.Unavailable);
        shareGap.State.ShouldBe(MetricState.Unavailable);
    }

    private static MemberSeries Moving(string symbol, double ret) => Series(symbol, 100, 100 * (1 + ret));

    [Fact]
    public void A_sector_return_is_the_equal_weight_mean_and_four_members_are_insufficient()
    {
        var sectors = new Dictionary<string, IReadOnlyList<MemberSeries>>
        {
            ["Banks"] = [Moving("a", 0.04), Moving("b", 0.02), Moving("c", 0.06), Moving("d", 0.0), Moving("e", 0.03)],
            ["Tiny"] = [Moving("f", 0.5), Moving("g", 0.5), Moving("h", 0.5), Moving("i", 0.5)],
            ["IT"] = [Moving("j", 0.01), Moving("k", 0.01), Moving("l", 0.01), Moving("m", 0.01), Moving("n", 0.01)],
            ["Auto"] = [Moving("o", 0.01), Moving("p", 0.01), Moving("q", 0.01), Moving("r", 0.01), Moving("s", 0.01)],
        };

        var rows = SectorStrength.Compute(sectors, D(1), [1], new StrengthOptions()).ToDictionary(r => r.Sector);

        rows["Banks"].Return.Number!.Value.ShouldBe(0.03, 1e-12);
        rows["Banks"].Rank.ShouldBe(1);
        rows["Tiny"].Return.State.ShouldBe(MetricState.Insufficient);
        rows["Tiny"].Members.ShouldBe(4);
        rows["Tiny"].Rank.ShouldBeNull();
        rows["IT"].Rank.ShouldBe(2);
        rows["Auto"].Rank.ShouldBe(2);
    }
}
