using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests;

/// <summary>The edges of the entry (the date and the delay of the fill minute) and the validation of the rules of the walker.</summary>
public sealed class TradeWalkerBoundaryTests
{
    private static Bar Minute(DateOnly date, string time, double price) => new(DayBuilder.Epoch(date, time), price, price + 0.5, price - 0.5, price, 1_000);

    [Fact]
    public void A_fill_minute_just_after_midnight_india_time_is_on_another_date_and_is_no_fill_bar_though_it_is_within_the_delay()
    {
        // The candidate's minute is 23:59 on Monday, so the decision instant is 00:00 on Tuesday and the fill minute 00:00 on Tuesday starts 0 minutes after it: the delay bound (5 minutes) passes, the
        // clock-of-day test (00:00 < 15:15) passes, and only the date clause refuses it. Without that clause this would be a trade entered at midnight.
        var monday = DayBuilder.Monday;
        var tuesday = monday.AddDays(1);
        var own = Minute(monday, "23:59", 100.00);
        var fill = Minute(tuesday, "00:00", 100.00);
        var candidate = Cand.Make(Side.Buy, 0, "23:59", 98.00, "23:45", (99.00, 100.50, 99.00, 100.50)) with { DecisionEpochSeconds = own.EpochSeconds + 60 };

        fill.EpochSeconds.ShouldBe(candidate.DecisionEpochSeconds, "the delay is 0 minutes, inside the bound");
        var outcome = TradeWalker.Walk(candidate, [own, fill], Cand.Standard).Value;

        outcome.NoTrade.ShouldBe(NoTradeReason.NoFillBar);
        outcome.Trade.ShouldBeNull();
    }

    [Theory]
    [InlineData("10:06", false)]
    [InlineData("10:07", true)]
    public void The_fill_minute_may_start_at_most_five_minutes_after_the_decision_instant(string fillAt, bool noFillBar)
    {
        // The candidate's minute is 10:00 (the decision instant is 10:01): a fill minute at 10:06 is 5 minutes late (allowed), one at 10:07 is 6 (no fill bar).
        var own = Minute(DayBuilder.Monday, "10:00", 100.00);
        var fill = Minute(DayBuilder.Monday, fillAt, 100.00);
        var candidate = Cand.Make(Side.Buy, 0, "10:01", 98.00, "09:45", (99.00, 100.50, 99.00, 100.50));

        var outcome = TradeWalker.Walk(candidate, [own, fill], Cand.Standard).Value;

        (outcome.NoTrade == NoTradeReason.NoFillBar).ShouldBe(noFillBar);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1440)]
    public void A_square_off_minute_outside_the_india_day_is_rules_invalid(int squareOffMinute)
    {
        var f1 = Cases.F("F1");

        var result = TradeWalker.Walk(f1.Items[0].Candidate, f1.Bars, Cand.Standard with { SquareOffMinute = squareOffMinute });

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("simulation.rules_invalid");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(915)]
    [InlineData(1439)]
    public void A_square_off_minute_inside_the_india_day_is_accepted(int squareOffMinute)
    {
        var f1 = Cases.F("F1");

        TradeWalker.Walk(f1.Items[0].Candidate, f1.Bars, Cand.Standard with { SquareOffMinute = squareOffMinute }).IsSuccess.ShouldBeTrue();
    }
}
