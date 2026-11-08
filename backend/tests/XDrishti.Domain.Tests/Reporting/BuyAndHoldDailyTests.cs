using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>CF-11 row 13 (ADR 0227): the day-by-day return of the equal-weight, no-rebalancing hold, worked by hand, and the cases where the same-universe hold cannot be had.</summary>
public sealed class BuyAndHoldDailyTests
{
    private static readonly DateOnly[] D = [new(2026, 1, 5), new(2026, 1, 6), new(2026, 1, 7), new(2026, 1, 8)];
    private static readonly BuyAndHoldOptions Options = new(3, 7, 0.25);

    private static InstrumentSeries Series(string symbol, params double[] closes) =>
        new(new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(symbol)).AsSpan(0, 16)), symbol, [.. closes.Select((c, i) => new DailyClose(D[i], c))]);

    private static List<InstrumentSeries> Three() => [Series("A", 100, 110, 110, 121), Series("B", 100, 100, 110, 110), Series("C", 100, 120, 120, 120)];

    [Fact]
    public void The_weights_drift_with_the_prices_and_nothing_is_rebalanced()
    {
        var result = BuyAndHoldDaily.Compute(Three(), D[0], D[3], D[1], Options);

        // value on D1 (1.10 + 1.00 + 1.20) / 3 = 1.10, on D2 (1.10 + 1.10 + 1.20) / 3 = 1.1333, on D3 (1.21 + 1.10 + 1.20) / 3 = 1.17, bought at D0
        result.Reason.ShouldBeEmpty();
        result.Days.ShouldBe([D[1], D[2], D[3]]);
        result.Returns.Count.ShouldBe(3);
        result.Returns[0].ShouldBe(0.10, 1e-12);
        result.Returns[1].ShouldBe((3.4 / 3 / 1.1) - 1, 1e-12);
        result.Returns[2].ShouldBe((1.17 / (3.4 / 3)) - 1, 1e-12);
    }

    [Fact]
    public void The_compounded_daily_returns_are_the_return_of_the_hold_over_the_window()
    {
        var result = BuyAndHoldDaily.Compute(Three(), D[0], D[3], D[1], Options);

        result.Returns.Aggregate(1.0, (value, r) => value * (1 + r)).ShouldBe(1.17, 1e-12);
    }

    [Fact]
    public void A_member_the_hold_cannot_use_makes_it_unavailable_and_is_named()
    {
        var series = Three();
        series[1] = Series("B", 100, 100, 0, 110); // a close that is not a positive number

        var result = BuyAndHoldDaily.Compute(series, D[0], D[3], D[1], Options);

        result.Days.ShouldBeEmpty();
        result.Returns.ShouldBeEmpty();
        result.Reason.ShouldContain("B");
        result.Reason.ShouldContain(BuyAndHold.BadPrice);
    }

    [Fact]
    public void An_unadjusted_jump_leaves_the_member_out_so_the_comparison_is_unavailable()
    {
        var series = Three();
        series[0] = Series("A", 100, 110, 55, 60); // a 50 % one-session move

        BuyAndHoldDaily.Compute(series, D[0], D[3], D[1], Options).Reason.ShouldContain(BuyAndHold.Discontinuity);
    }

    [Fact]
    public void Fewer_members_than_the_minimum_is_unavailable()
    {
        var result = BuyAndHoldDaily.Compute(Three().Take(2).ToList(), D[0], D[3], D[1], Options);

        result.Returns.ShouldBeEmpty();
        result.Reason.ShouldContain("at least 3");
    }

    [Fact]
    public void There_must_be_a_purchase_close_before_the_first_day_asked()
    {
        var result = BuyAndHoldDaily.Compute(Three(), D[0], D[3], D[0], Options);

        result.Returns.ShouldBeEmpty();
        result.Reason.ShouldContain("purchase");
    }

    [Fact]
    public void A_day_on_which_a_member_has_no_close_is_not_a_day_of_the_hold()
    {
        var series = Three();
        series[2] = new InstrumentSeries(series[2].Id, "C", [new DailyClose(D[0], 100), new DailyClose(D[1], 120), new DailyClose(D[3], 120)]);

        var result = BuyAndHoldDaily.Compute(series, D[0], D[3], D[1], Options);

        result.Days.ShouldBe([D[1], D[3]]);
        result.Returns[0].ShouldBe(0.10, 1e-12);
        result.Returns[1].ShouldBe((1.17 / 1.10) - 1, 1e-12, "the value on D3 against the value on D1: nothing is invented for D2");
    }

    [Fact]
    public void Two_closes_on_one_day_are_unavailable_and_never_repaired()
    {
        var series = Three();
        series[0] = new InstrumentSeries(series[0].Id, "A", [.. series[0].Closes, new DailyClose(D[2], 111)]);

        BuyAndHoldDaily.Compute(series, D[0], D[3], D[1], Options).Reason.ShouldContain("two daily closes");
    }

    [Fact]
    public void An_empty_universe_is_unavailable() => BuyAndHoldDaily.Compute([], D[0], D[3], D[1], Options).Returns.ShouldBeEmpty();
}
