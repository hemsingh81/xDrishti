using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;

namespace XDrishti.Simulation.Tests.Features;

/// <summary>The decision-time features (spec P6-04, ADR 0066): what each feature is, and the property that nothing at or after the decision instant can change it.</summary>
public sealed class TradeFeatureExtractorTests
{
    private static readonly long Monday = new DateTimeOffset(2025, 3, 3, 9, 15, 0, TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();
    private static readonly TradingCalendar Calendar = new();

    /// <summary>Five regular sessions (Monday to Friday) of seeded 1-minute bars.</summary>
    private static Bar[] Week(int seed)
    {
        var state = (ulong)seed * 6_364_136_223_846_793_005UL + 1_442_695_040_888_963_407UL;
        double Next()
        {
            state = (state * 6_364_136_223_846_793_005UL) + 1_442_695_040_888_963_407UL;
            return (state >> 11) / (double)(1UL << 53);
        }

        var bars = new List<Bar>();
        var price = 100.0;
        for (var day = 0; day < 5; day++)
        {
            for (var minute = 0; minute < 375; minute++)
            {
                var open = price;
                price = Math.Max(1, price + ((Next() - 0.5) * 0.4));
                var high = Math.Max(open, price) + (Next() * 0.1);
                var low = Math.Min(open, price) - (Next() * 0.1);
                bars.Add(new Bar(Monday + (day * 86_400L) + (minute * 60L), Math.Round(open, 4), Math.Round(high, 4), Math.Round(low, 4), Math.Round(price, 4), 1_000 + (long)(Next() * 500)));
            }
        }

        return [.. bars];
    }

    // the decision at the close of the 15-minute bar that ends minutesAfterOpen minutes after the open of the day
    private static long Decision(int day, int minutesAfterOpen) => Monday + (day * 86_400L) + (minutesAfterOpen * 60L);

    private static TradeFeatureDecision Fifteen(string key, int day, int minutesAfterOpen, Side side = Side.Buy, double stop = 99, double? target = 103) =>
        new(key, Timeframe.M15, Decision(day, minutesAfterOpen), side, stop, target);

    private static IReadOnlyDictionary<string, double[]> Extract(IReadOnlyList<Bar> bars, params TradeFeatureDecision[] decisions) =>
        TradeFeatureExtractor.Extract(bars, AdjustmentFactors.None, Calendar, decisions);

    private static double Of(double[] values, string name) => values[TradeFeatureCatalog.IndexOf(name)];

    [Fact]
    public void A_decision_late_in_the_week_has_every_default_required_bar_and_plan_feature_known()
    {
        var bars = Week(1);

        var values = Extract(bars, Fifteen("a", 3, 135))["a"];

        values.Length.ShouldBe(TradeFeatureCatalog.StoredCount);
        foreach (var name in TradeFeatureCatalog.DefaultRequired.Where(n => TradeFeatureCatalog.IndexOf(n) < TradeFeatureCatalog.StoredCount))
        {
            double.IsNaN(Of(values, name)).ShouldBeFalse(name + " should be known");
        }
    }

    [Fact]
    public void The_plan_features_follow_the_decision_the_stop_and_the_target()
    {
        var bars = Week(2);
        var decision = Fifteen("a", 3, 135, Side.Sell, stop: 101, target: 97);
        var close = bars.Single(b => b.EpochSeconds == decision.DecisionEpochSeconds - 60).Close;

        var values = Extract(bars, decision)["a"];

        Of(values, "day_of_week").ShouldBe(4, "2025-03-06 is a Thursday");
        Of(values, "minute_of_day").ShouldBe((9 * 60) + 15 + 135);
        Of(values, "side_sign").ShouldBe(-1);
        Of(values, "risk_pct").ShouldBe(Math.Abs(close - 101) / close * 100, 1e-9);
        Of(values, "planned_rr").ShouldBe(Math.Abs(97 - close) / Math.Abs(close - 101), 1e-9);
    }

    [Fact]
    public void A_decision_without_a_target_has_an_unknown_planned_reward_to_risk()
    {
        var values = Extract(Week(3), Fifteen("a", 3, 135, target: null))["a"];

        double.IsNaN(Of(values, "planned_rr")).ShouldBeTrue();
    }

    [Fact]
    public void The_context_features_are_unknown_without_a_market_context()
    {
        var values = Extract(Week(4), Fifteen("a", 3, 135))["a"];

        foreach (var def in TradeFeatureCatalog.All.Where(d => d.Source == TradeFeatureSource.Context))
        {
            double.IsNaN(Of(values, def.Name)).ShouldBeTrue(def.Name);
        }
    }

    [Fact]
    public void The_first_session_has_no_previous_day_so_those_features_are_unknown_not_invented()
    {
        var values = Extract(Week(5), Fifteen("a", 0, 135))["a"];

        double.IsNaN(Of(values, "gap_pct")).ShouldBeTrue();
        double.IsNaN(Of(values, "prev_close_dist_pct")).ShouldBeTrue();
        double.IsNaN(Of(values, "prev_day_range_pct")).ShouldBeTrue();
    }

    [Fact]
    public void A_daily_decision_does_not_throw_and_leaves_the_session_bound_features_unknown()
    {
        var bars = Week(6);
        var decision = new TradeFeatureDecision("d", Timeframe.D1, Monday + (3 * 86_400L) + (375 * 60L), Side.Buy, 99, 103);

        var values = Extract(bars, decision)["d"];

        values.Length.ShouldBe(TradeFeatureCatalog.StoredCount);
        double.IsNaN(Of(values, "vwap_dist_pct")).ShouldBeTrue("vwap does not exist on a daily bar");
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    public void No_feature_changes_when_the_bars_are_cut_at_the_decision_instant_or_replaced_after_it(int seed)
    {
        var bars = Week(seed);
        var decisions = new[] { Fifteen("a", 2, 90), Fifteen("b", 3, 135, Side.Sell, 101, 97), Fifteen("c", 3, 225), Fifteen("d", 4, 30) };
        var full = Extract(bars, decisions);

        foreach (var d in decisions)
        {
            var truncated = bars.Where(b => b.EpochSeconds < d.DecisionEpochSeconds).ToArray();
            var replaced = bars.Select(b => b.EpochSeconds < d.DecisionEpochSeconds ? b : new Bar(b.EpochSeconds, 9_999, 99_999, 0.01, 5_555, 123_456_789)).ToArray();

            var cut = Extract(truncated, d)[d.TradeKey];
            var wild = Extract(replaced, d)[d.TradeKey];

            cut.ShouldBe(full[d.TradeKey], $"seed {seed}, {d.TradeKey}: cutting the bars at the decision changed a feature");
            wild.ShouldBe(full[d.TradeKey], $"seed {seed}, {d.TradeKey}: bars after the decision changed a feature");
        }
    }

    [Fact]
    public void Control_a_bar_before_the_decision_does_change_the_features_so_the_property_is_not_vacuous()
    {
        var bars = Week(21);
        var d = Fifteen("a", 3, 135);
        var before = Extract(bars, d)["a"];
        var bumped = bars.Select(b => b.EpochSeconds == d.DecisionEpochSeconds - 60 ? b with { Close = b.Close + 1, High = b.High + 1 } : b).ToArray();

        var after = Extract(bumped, d)["a"];

        after.ShouldNotBe(before);
    }

    [Fact]
    public void Decisions_of_other_trades_do_not_change_a_trades_features()
    {
        var bars = Week(22);
        var a = Fifteen("a", 3, 135);
        var alone = Extract(bars, a)["a"];

        var together = Extract(bars, Fifteen("z", 4, 300), a, Fifteen("m", 1, 45), new TradeFeatureDecision("w", Timeframe.M5, Decision(2, 60), Side.Buy, 99, 103))["a"];

        together.ShouldBe(alone);
    }

    [Fact]
    public void A_corporate_action_after_the_decision_day_halves_every_fed_price_and_leaves_the_ratio_features_alone()
    {
        var bars = Week(23);
        var d = Fifteen("a", 3, 135);
        var plain = Extract(bars, d)["a"];
        var factors = AdjustmentFactors.From([new AppliedAction(new DateOnly(2025, 3, 7), CorporateActionKind.Split, 1, 2, 0.5m)]);

        var adjusted = TradeFeatureExtractor.Extract(bars, factors, Calendar, [d])["a"];

        Of(adjusted, "bar_range_pct").ShouldBe(Of(plain, "bar_range_pct"), 0.05);
        Of(adjusted, "rsi14").ShouldBe(Of(plain, "rsi14"), 1.0);
    }
}
