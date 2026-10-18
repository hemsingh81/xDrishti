using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Scanning;

/// <summary>Criterion 9 of P3-03a: the calendar reaches the frame (a special session closes its last bucket at its own last minute), and <c>FullyReadySteps</c> counts the window steps at which every feature of setup and trigger has a value.</summary>
public sealed class CalendarAndReadinessTests
{
    private static TradingCalendar SpecialSession() =>
        new([TradingDay.Special(Day.Monday, 9 * 60 + 15, 12 * 60 + 20, CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);

    private static Bar[] Cut() => Day.P().B().Set("10:05 100.50/100.50/99.50/100.50").EndsAfter("12:19").Bars();

    [Fact]
    public void A_special_session_closes_its_last_bucket_at_its_own_last_minute_when_the_scanner_has_the_calendar()
    {
        var bars = Cut();
        bars.Length.ShouldBe(185);

        var with = Scan.Run(OrbV1.Create(), Side.Buy, bars, calendar: SpecialSession()).Counters;
        var without = Scan.Run(OrbV1.Create(), Side.Buy, bars).Counters;

        // The session ends at 12:20, in the middle of the 12:15 bucket. With the calendar that bucket closes at the session's own last minute (12:19) and is decided at 12:20; without it only the regular 15:30
        // close is known and the bucket never closes. (The spec's example, a session to 12:30 with 195 minutes, ends on a bucket boundary, so the calendar cannot make a difference there: see the report.)
        (with.DecisionSteps, with.WindowSteps, with.OutOfWindowSteps).ShouldBe((13, 9, 4));
        (without.DecisionSteps, without.WindowSteps, without.OutOfWindowSteps).ShouldBe((12, 9, 3));
    }

    private static XDrishti.Strategies.Model.StrategyDefinition AboveEma(string setup) => TestStrategies.OrbBuyOnly().WithBuy(b => b with { Setup = TestStrategies.All(setup) });

    private static Bar[] TwoDays() => Day.Join(Fixtures.F8(), Day.P(new DateOnly(2026, 3, 3)).Set("09:44 99.00/100.00/99.00/100.00"));

    [Fact]
    public void An_average_that_needs_30_bars_is_not_ready_on_a_first_25_bar_day_and_is_from_the_fifth_bar_of_the_second()
    {
        var days = TwoDays();

        var firstDay = Scan.Run(AboveEma("close > ema(30)"), Side.Buy, days.Take(375)).Counters;
        var both = Scan.Run(AboveEma("close > ema(30)"), Side.Buy, days);

        firstDay.FullyReadySteps.ShouldBe(0);
        both.Counters.FullyReadySteps.ShouldBe(5, "10:30, 10:45, 11:00, 11:15 and 11:30 of the second day");
        Identities.ShouldHold(both);
    }

    [Fact]
    public void A_condition_made_false_by_logic_while_a_feature_is_empty_does_not_count_as_ready()
    {
        var scanner = Scan.Run(AboveEma("close > ema(30) and time >= \"10:00\""), Side.Buy, TwoDays().Take(375));

        // at 09:30 and 09:45 the clock makes the setup False although the average is empty
        Identities.Of(scanner.Counters, "buy.setup[0]").False.ShouldBeGreaterThanOrEqualTo(2);
        scanner.Counters.FullyReadySteps.ShouldBe(0);
    }
}
