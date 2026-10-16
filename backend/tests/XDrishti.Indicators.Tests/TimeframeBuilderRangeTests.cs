using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// A bar whose India date lies outside the range of <see cref="DateOnly"/> (years 1 to 9999) — a feed that sends epoch milliseconds instead of seconds is the plausible cause — is bad input, and bad input is never
/// thrown (review of P3-01c, M1 and M12): no timeframe builder throws for it, with or without a calendar, and the frame that holds the lane is not left half-stepped. Outside the range the calendar knows nothing, so
/// a week or month there is closed by the first bar of a later period, as without a calendar; the months and weeks themselves follow the Gregorian rules without end.
/// </summary>
public sealed class TimeframeBuilderRangeTests
{
    private const long SecondsPerDay = 86_400;

    /// <summary>One Gregorian cycle: 146,097 days, a whole number of weeks, so a date and the same date a cycle later have the same weekday and the same position in their month.</summary>
    private const long CycleDays = 146_097;

    /// <summary>12,675 years after 1970 and the same before it: far outside years 1 to 9999.</summary>
    private const long FarFuture = 400_000_000_000L;

    private static readonly Timeframe[] AllBuilt = [Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1];

    public static TheoryData<Timeframe, bool, long> Cases
    {
        get
        {
            var data = new TheoryData<Timeframe, bool, long>();
            foreach (var timeframe in AllBuilt)
            {
                foreach (var withCalendar in new[] { false, true })
                {
                    foreach (var epoch in OutsideTheRange())
                    {
                        data.Add(timeframe, withCalendar, epoch);
                    }
                }
            }

            return data;
        }
    }

    /// <summary>A far future, a far past, and the very first India midnight after 9999-12-31 and before 0001-01-01 (the edges of the range).</summary>
    private static long[] OutsideTheRange() =>
    [
        FarFuture,
        -FarFuture,
        IndiaDays.MidnightOf(DateOnly.MaxValue.DayNumber - IndiaDays.UnixEpochDayNumber + 1L),
        IndiaDays.MidnightOf(-IndiaDays.UnixEpochDayNumber - 1L),
    ];

    private static TradingCalendar? CalendarOf(bool present) => present ? new TradingCalendar() : null;

    [Theory]
    [MemberData(nameof(Cases))]
    public void No_builder_throws_for_bars_outside_the_range_of_dates_with_or_without_a_calendar(Timeframe timeframe, bool withCalendar, long epoch)
    {
        var builder = new TimeframeBuilder(timeframe, CalendarOf(withCalendar));
        var bars = new[]
        {
            new Bar(epoch, 1, 2, 0.5, 1.5, 10),
            new Bar(epoch + 60, 1.5, 3, 1, 2, 20),
            new Bar(epoch + 40 * SecondsPerDay, 2, 4, 1.5, 3, 30),
            new Bar(epoch + 500 * SecondsPerDay, 3, 5, 2, 4, 40),
        };
        var closed = 0;

        foreach (var bar in bars)
        {
            closed += builder.Push(bar, out _, out _);
        }

        closed.ShouldBeGreaterThan(0, "the bars of a date outside the range are bars like any other: the later dates close what the earlier began");
        builder.Ignored.ShouldBe(0, "nothing was out of order");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_largest_and_smallest_epochs_of_a_long_do_not_throw_either(bool withCalendar)
    {
        foreach (var timeframe in AllBuilt)
        {
            var builder = new TimeframeBuilder(timeframe, CalendarOf(withCalendar));
            var low = new Bar(long.MinValue, 1, 1, 1, 1, 1);
            var high = new Bar(long.MaxValue, 1, 1, 1, 1, 1);

            Should.NotThrow(() => builder.Push(low, out _, out _), $"{timeframe.ToCode()} at the smallest epoch");
            Should.NotThrow(() => builder.Push(high, out _, out _), $"{timeframe.ToCode()} at the largest epoch");
        }
    }

    [Theory]
    [InlineData(Timeframe.W1, false)]
    [InlineData(Timeframe.W1, true)]
    [InlineData(Timeframe.Mo1, false)]
    [InlineData(Timeframe.Mo1, true)]
    public void A_week_or_month_outside_the_range_holds_its_bars_and_is_closed_by_the_first_bar_of_a_later_period(Timeframe timeframe, bool withCalendar)
    {
        var builder = new TimeframeBuilder(timeframe, CalendarOf(withCalendar));
        var first = new Bar(FarFuture, 100, 110, 90, 105, 10);
        var second = new Bar(FarFuture + 60, 105, 120, 101, 118, 20);
        var later = new Bar(FarFuture + 400 * SecondsPerDay, 118, 119, 117, 118, 5);

        builder.Push(first, out _, out _).ShouldBe(0);
        builder.Push(second, out _, out _).ShouldBe(0, "a calendar has no session for a date it cannot name: the period waits for a later period, it is not closed early");
        var count = builder.Push(later, out var period, out _);

        count.ShouldBe(1);
        period.Timeframe.ShouldBe(timeframe);
        period.Bar.ShouldBe(new Bar(period.StartEpochSeconds, 100, 120, 90, 118, 30));
        period.Bars.ShouldBe(2);
        period.StartEpochSeconds.ShouldBeLessThanOrEqualTo(FarFuture);
        IndiaDays.MinuteOfDay(period.StartEpochSeconds).ShouldBe(0, "a period starts at India midnight");
    }

    [Theory]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    public void A_period_outside_the_range_starts_where_the_same_date_a_whole_number_of_400_year_cycles_earlier_starts(Timeframe timeframe)
    {
        // 31 cycles (12,400 years) back from the far future lands in the year 1800 or so: a date the platform's calendar can answer for. The weekday and the day of the month repeat every cycle.
        const long Cycles = 32;
        var shift = Cycles * CycleDays * SecondsPerDay;
        var inRange = new TimeframeBuilder(timeframe);
        var outside = new TimeframeBuilder(timeframe);
        DateOnly.FromDayNumber((int)(IndiaDays.DayOf(FarFuture - shift) + IndiaDays.UnixEpochDayNumber)).Year.ShouldBeInRange(1, 9999);

        for (var step = 0; step < 4; step++)
        {
            var offset = step * 17 * SecondsPerDay + step * 60;
            var near = new Bar(FarFuture - shift + offset, 1 + step, 2 + step, 0.5, 1.5 + step, 10 + step);
            var far = new Bar(FarFuture + offset, 1 + step, 2 + step, 0.5, 1.5 + step, 10 + step);

            var nearCount = inRange.Push(near, out var nearBar, out _);
            var farCount = outside.Push(far, out var farBar, out _);

            farCount.ShouldBe(nearCount, $"step {step}");
            if (farCount > 0)
            {
                farBar.StartEpochSeconds.ShouldBe(nearBar.StartEpochSeconds + shift, $"step {step}: the period starts on the same weekday or day of the month");
                (farBar.Bar.Open, farBar.Bar.High, farBar.Bar.Low, farBar.Bar.Close, farBar.Bar.Volume, farBar.Bars).ShouldBe((nearBar.Bar.Open, nearBar.Bar.High, nearBar.Bar.Low, nearBar.Bar.Close, nearBar.Bar.Volume, nearBar.Bars));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_frame_steps_a_bar_outside_the_range_on_every_lane_without_throwing_and_is_not_left_half_stepped(bool withCalendar)
    {
        FeatureSpec[] specs =
        [
            new("close", Timeframe.M5), new("close", Timeframe.H1), new("close", Timeframe.D1), new("close", Timeframe.W1), new("close", Timeframe.Mo1), new("sma", Timeframe.Mo1, PriceSource.Close, 3),
        ];
        var frame = FeatureFrame.Create(specs, new FrameOptions(CalendarOf(withCalendar))).Value;

        frame.Step(new Bar(FarFuture, 1, 1, 1, 1, 1));
        frame.Step(new Bar(FarFuture + 60, 2, 2, 2, 2, 1));
        frame.Step(new Bar(FarFuture + 400 * SecondsPerDay, 3, 3, 3, 3, 1));

        frame.Steps.ShouldBe(3, "every bar was a step: none was dropped half way");
        frame.IgnoredBars.ShouldBe(0);
        frame.BarClosed(Timeframe.Mo1).ShouldBeTrue("the later month closed the earlier one");
        frame[4].ShouldBe(2, "the close of the month that closed");
    }
}
