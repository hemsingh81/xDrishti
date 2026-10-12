using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

/// <summary>Spotting a wrong reading of Dhan's timestamps from the bars of one window: another time base, or the India/UTC offset (nothing is ever shifted).</summary>
public sealed class IntradayAlignmentCheckTests
{
    /// <summary>Monday 2026-10-12 12:00 India time, and a window of five days before.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly FetchWindow Window = new(Now.AddDays(-5), Now);

    private static long At(int day, int minuteOfDay) =>
        new DateTimeOffset(2026, 10, day, 0, 0, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds() + (minuteOfDay * 60L);

    private static Bar B(long seconds) => new(seconds, 100, 101, 99, 100, 10);

    private static List<Bar> FullSession(int day, long shiftSeconds = 0) =>
        [.. Enumerable.Range(SessionClock.OpenMinute, SessionClock.RegularBars).Select(m => B(At(day, m) + shiftSeconds))];

    [Fact]
    public void No_bars_nothing_to_say()
    {
        IntradayAlignmentCheck.Problem([], Window).ShouldBeNull();
    }

    [Fact]
    public void Whole_correctly_dated_sessions_are_fine()
    {
        IntradayAlignmentCheck.Problem([.. FullSession(8), .. FullSession(9)], Window).ShouldBeNull();
    }

    [Fact]
    public void A_special_evening_session_alone_is_fine()
    {
        var muhurat = Enumerable.Range(18 * 60 + 15, 60).Select(m => B(At(9, m))).ToList();

        IntradayAlignmentCheck.Problem(muhurat, Window).ShouldBeNull();
    }

    [Theory]
    [InlineData(330 * 60, "subtracting 5 h 30 min")] // India time delivered as if it were UTC: too late by 5 h 30
    [InlineData(-330 * 60, "adding 5 h 30 min")]
    public void A_whole_session_delivered_with_the_india_utc_offset_is_named(long shiftSeconds, string wording)
    {
        // For +5 h 30 a full day lands on 14:45-20:59: a good part of it inside the evening window, and the shift is still found.
        var problem = IntradayAlignmentCheck.Problem(FullSession(9, shiftSeconds), Window);

        problem.ShouldNotBeNull();
        problem.ShouldContain(wording);
        problem.ShouldContain("nothing was shifted or stored");
    }

    [Fact]
    public void Bars_outside_a_session_that_no_5h30_shift_explains_are_left_to_the_row_checks()
    {
        var odd = new[] { 3 * 60, 4 * 60, 5 * 60, 9 * 60 + 15 }.Select(m => B(At(9, m))).ToList();

        IntradayAlignmentCheck.Problem(odd, Window).ShouldBeNull();
    }

    [Fact]
    public void A_shift_that_explains_only_some_bars_is_not_enough()
    {
        var mixed = FullSession(9, 330 * 60);
        mixed.Add(B(At(9, 3 * 60))); // still outside after any shift

        IntradayAlignmentCheck.Problem(mixed, Window).ShouldBeNull();
    }

    [Fact]
    public void Most_bars_in_the_session_with_a_few_strays_is_fine()
    {
        var bars = FullSession(9);
        bars.Add(B(At(9, 3 * 60)));

        IntradayAlignmentCheck.Problem(bars, Window).ShouldBeNull();
    }

    [Fact]
    public void Bars_dated_years_away_from_the_period_asked_for_name_the_time_base()
    {
        var oldEpoch = FullSession(9, -3652L * 86400);

        var problem = IntradayAlignmentCheck.Problem(oldEpoch, Window);

        problem.ShouldNotBeNull();
        problem.ShouldContain("time base");
        problem.ShouldContain("2026-10-07 to 2026-10-12");
        problem.ShouldContain("dated 2016-");
    }

    [Fact]
    public void Bars_stamped_with_the_minute_they_end_are_named()
    {
        var endLabelled = FullSession(8, 60); // 09:16 … 15:30

        var problem = IntradayAlignmentCheck.Problem(endLabelled, Window);

        problem.ShouldNotBeNull();
        problem.ShouldContain("END of each minute");
    }

    [Fact]
    public void A_session_that_ends_at_the_last_minute_with_its_opening_bar_is_fine()
    {
        IntradayAlignmentCheck.Problem(FullSession(8).Where(b => SessionClock.MinuteOfDay(b.EpochSeconds) != SessionClock.OpenMinute + 3).ToList(), Window).ShouldBeNull();
    }

    [Fact]
    public void A_day_of_slack_around_the_period_is_allowed()
    {
        var before = FullSession(6); // the window starts on the 7th at 09:15; the 6th is within a day of it

        IntradayAlignmentCheck.Problem(before, Window).ShouldBeNull();
        IntradayAlignmentCheck.Problem(FullSession(3), Window).ShouldNotBeNull();
    }
}
