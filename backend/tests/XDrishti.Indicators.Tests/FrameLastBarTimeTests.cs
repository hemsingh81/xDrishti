using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// <see cref="FeatureFrame.LastBarTime"/> (P3-02, item 0): the start in epoch seconds of the last accepted 1-minute bar, 0 before the first step, unchanged by a bar the ordering guard skipped, cleared by <c>Reset</c>,
/// and the same for a frame whatever timeframes it was asked for. The epoch seconds in the hand-calculated cases are written out (Monday 2 March 2026 09:15 India time is 03:45 UTC, 1,772,423,100), not taken from
/// the test helpers that build the bars.
/// </summary>
public sealed class FrameLastBarTimeTests
{
    /// <summary>Monday 2 March 2026, 09:15 India time (03:45 UTC): the start of the first minute of the first fixture session.</summary>
    private const long MondayOpen = 1_772_423_100;

    public static TheoryData<string> Shapes { get; } =
        ["one-minute", "five-minute only", "hourly only", "daily only", "weekly only", "monthly only", "date features only", "all lanes"];

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 20)];

    /// <summary>The week of Monday 2 March 2026 (Friday the 6th is a holiday) and the month of January 2026 (ending on Friday the 30th), with and without a calendar; the step that closes each, its bar and the first bar of the next period.</summary>
    public static TheoryData<string, Timeframe, bool, int, long> ClosingSteps { get; } = new()
    {
        { "holiday friday", Timeframe.W1, true, 1_499, 1_772_704_740 },
        { "holiday friday", Timeframe.W1, false, 1_500, 1_773_027_900 },
        { "month ends on a weekend", Timeframe.Mo1, true, 1_874, 1_769_767_140 },
        { "month ends on a weekend", Timeframe.Mo1, false, 1_875, 1_770_003_900 },
    };

    private static FrameOptions WithExpiryDays() => new(ExpiryDays: new HashSet<DateOnly>());

    private static FeatureFrame Frame(string shape, FrameOptions? options = null) => FeatureFrame.Create(
        shape switch
        {
            "one-minute" => [new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3)],
            "five-minute only" => [new FeatureSpec("close", Timeframe.M5)],
            "hourly only" => [new FeatureSpec("close", Timeframe.H1)],
            "daily only" => [new FeatureSpec("close", Timeframe.D1)],
            "weekly only" => [new FeatureSpec("close", Timeframe.W1)],
            "monthly only" => [new FeatureSpec("close", Timeframe.Mo1)],
            "date features only" => [new FeatureSpec("day_of_week"), new FeatureSpec("is_expiry_day")],
            "all lanes" =>
            [
                new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3),
                new FeatureSpec("close", Timeframe.M5),
                new FeatureSpec("close", Timeframe.H1),
                new FeatureSpec("close", Timeframe.D1),
                new FeatureSpec("close", Timeframe.W1),
                new FeatureSpec("close", Timeframe.Mo1),
                new FeatureSpec("day_of_week"),
                new FeatureSpec("is_expiry_day"),
            ],
            _ => throw new ArgumentException("Unknown shape " + shape),
        },
        options ?? WithExpiryDays()).Value;

    [Theory]
    [MemberData(nameof(Shapes))]
    public void It_is_zero_before_the_first_step_whatever_the_frame_is_asked_for(string shape)
    {
        var frame = Frame(shape);

        frame.LastBarTime.ShouldBe(0);
        frame.Steps.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void It_is_the_start_of_the_bar_just_accepted_after_every_step_also_for_a_frame_without_a_one_minute_feature(string shape)
    {
        var frame = Frame(shape);
        var bars = BarFixtures.Get("three consecutive days");

        for (var i = 0; i < bars.Count; i++)
        {
            frame.Step(bars[i]);

            if (frame.LastBarTime != bars[i].EpochSeconds)
            {
                Assert.Fail($"{shape}: after step {i} LastBarTime is {frame.LastBarTime}, the bar started at {bars[i].EpochSeconds}");
            }
        }

        frame.Steps.ShouldBe(bars.Count);
        frame.IgnoredBars.ShouldBe(0);
    }

    [Fact]
    public void The_first_minutes_of_a_session_give_the_hand_calculated_epoch_seconds()
    {
        var frame = Frame("one-minute");
        var bars = Minutes.Closes(BarFixtures.Mon, 1, 2, 3);

        frame.Step(bars[0]);
        var first = frame.LastBarTime;
        frame.Step(bars[1]);
        var second = frame.LastBarTime;
        frame.Step(bars[2]);
        var third = frame.LastBarTime;

        first.ShouldBe(MondayOpen, "the minute 09:15 starts at 03:45 UTC");
        second.ShouldBe(MondayOpen + 60);
        third.ShouldBe(MondayOpen + 120, "the minute 09:17; it closed at 09:18 India time, MondayOpen + 180");
    }

    [Fact]
    public void A_gap_between_two_accepted_bars_is_kept_it_is_the_bar_fed_not_the_next_minute()
    {
        var frame = Frame("one-minute");
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open, 1));

        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.At(11, 0), 2));

        frame.LastBarTime.ShouldBe(MondayOpen + 105 * 60, "11:00 is 105 minutes after 09:15");
        frame.Steps.ShouldBe(2);
    }

    [Fact]
    public void A_higher_timeframe_close_does_not_move_it_to_the_start_of_the_higher_bar()
    {
        var frame = Frame("five-minute only");
        var bars = Minutes.Closes(BarFixtures.Mon, 1, 2, 3, 4, 5);

        for (var i = 0; i < 4; i++)
        {
            frame.Step(bars[i]);
            frame.BarClosed(Timeframe.M5).ShouldBeFalse($"minute {i}");
        }

        frame.LastBarTime.ShouldBe(MondayOpen + 3 * 60);
        frame.Step(bars[4]);

        frame.BarClosed(Timeframe.M5).ShouldBeTrue("the 09:19 minute closes the 09:15 to 09:20 bucket");
        frame.TryLastClosed(Timeframe.M5, out var bucket).ShouldBeTrue();
        bucket.StartEpochSeconds.ShouldBe(MondayOpen, "the five-minute bar starts at 09:15");
        frame.LastBarTime.ShouldBe(MondayOpen + 4 * 60, "the frame's clock is the minute that closed it, 09:19");
    }

    [Theory]
    [MemberData(nameof(ClosingSteps))]
    public void A_weekly_or_monthly_frame_keeps_the_minute_of_the_step_that_closes_the_period(string fixture, Timeframe timeframe, bool calendar, int step, long expected)
    {
        var series = BarFixtures.GetPeriods(fixture);
        var options = PeriodCalendars.For(series, calendar ? CalendarKnowledge.Full : CalendarKnowledge.None).Options();
        var frame = FeatureFrame.Create([new FeatureSpec("close", timeframe)], options).Value;
        int? closedAt = null;

        for (var i = 0; i < series.Bars.Count && closedAt is null; i++)
        {
            frame.Step(series.Bars[i]);
            if (frame.BarClosed(timeframe))
            {
                closedAt = i;
            }
        }

        closedAt.ShouldBe(step, $"{fixture}, calendar {calendar}: the step that closes the first {timeframe.ToCode()} bar");
        series.Bars[step].EpochSeconds.ShouldBe(expected, "the hand-calculated bar of that step");
        frame.LastBarTime.ShouldBe(expected);
    }

    [Fact]
    public void One_step_before_the_week_closes_it_is_the_minute_before_the_closing_one()
    {
        var series = BarFixtures.GetPeriods("holiday friday");
        var frame = FeatureFrame.Create([new FeatureSpec("close", Timeframe.W1)], PeriodCalendars.For(series, CalendarKnowledge.Full).Options()).Value;

        for (var i = 0; i < 1_499; i++)
        {
            frame.Step(series.Bars[i]);
        }

        frame.BarClosed(Timeframe.W1).ShouldBeFalse();
        frame.LastBarTime.ShouldBe(1_772_704_680, "Thursday 5 March 2026, 15:28 India time");
    }

    [Fact]
    public void A_duplicate_and_an_older_bar_leave_it_where_it_was_and_the_next_accepted_bar_moves_it()
    {
        var frame = Frame("all lanes");
        var bars = Minutes.Closes(BarFixtures.Mon, 1, 2, 3, 4);
        for (var i = 0; i < 3; i++)
        {
            frame.Step(bars[i]);
        }

        frame.Step(bars[2] with { Close = 999, Volume = 1_000_000 });
        var afterDuplicate = frame.LastBarTime;
        frame.Step(bars[0]);
        var afterOlder = frame.LastBarTime;
        var ignored = frame.IgnoredBars;
        frame.Step(bars[3]);

        afterDuplicate.ShouldBe(MondayOpen + 120);
        afterOlder.ShouldBe(MondayOpen + 120);
        ignored.ShouldBe(2);
        frame.LastBarTime.ShouldBe(MondayOpen + 180);
        frame.Steps.ShouldBe(4);
    }

    [Fact]
    public void An_older_bar_of_a_previous_session_does_not_move_it_back()
    {
        var frame = Frame("daily only");
        var tuesday = Minutes.Close(BarFixtures.Tue, Minutes.Open, 10);
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.At(15, 29), 9));
        frame.Step(tuesday);

        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.At(15, 28), 8));

        frame.LastBarTime.ShouldBe(1_772_509_500, "Tuesday 3 March 2026, 09:15 India time");
        frame.LastBarTime.ShouldBe(tuesday.EpochSeconds);
        frame.IgnoredBars.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Reset_sets_it_back_to_zero_and_the_frame_takes_an_earlier_bar_again(string shape)
    {
        var frame = Frame(shape);
        var tuesday = Minutes.Closes(BarFixtures.Tue, 1, 2, 3);
        foreach (var bar in tuesday)
        {
            frame.Step(bar);
        }

        frame.Step(tuesday[0]);
        frame.IgnoredBars.ShouldBe(1);
        frame.LastBarTime.ShouldBe(tuesday[2].EpochSeconds);

        frame.Reset();
        var cleared = frame.LastBarTime;
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open, 5));

        cleared.ShouldBe(0, shape);
        frame.LastBarTime.ShouldBe(MondayOpen, "a Monday bar is accepted after a Tuesday run once the guard is cleared");
        frame.IgnoredBars.ShouldBe(0);
        frame.Steps.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void With_repeated_and_older_bars_mixed_in_it_is_always_the_latest_start_fed_so_far(int seed)
    {
        var series = SeriesGenerator.Thin(seed, 150);
        var random = new Random(seed * 17);
        var fed = new List<Bar>();
        for (var i = 0; i < series.Bars.Count; i++)
        {
            fed.Add(series.Bars[i]);
            if (random.NextDouble() < 0.1)
            {
                fed.Add(series.Bars[random.Next(0, i + 1)] with { Close = 12_345 });
            }
        }

        var frame = Frame("all lanes", new FrameOptions(PeriodCalendars.For(series, seed % 2 == 0 ? CalendarKnowledge.Full : CalendarKnowledge.None).Calendar, ExpiryDays: new HashSet<DateOnly>()));
        var latest = 0L;
        var accepted = 0;
        for (var i = 0; i < fed.Count; i++)
        {
            // a bar is accepted exactly when it starts after every bar before it
            if (fed[i].EpochSeconds > latest)
            {
                latest = fed[i].EpochSeconds;
                accepted++;
            }

            frame.Step(fed[i]);

            if (frame.LastBarTime != latest)
            {
                Assert.Fail($"seed {seed}, step {i}: LastBarTime is {frame.LastBarTime}, the latest start fed so far is {latest}");
            }
        }

        frame.Steps.ShouldBe(accepted);
        frame.IgnoredBars.ShouldBe(fed.Count - accepted);
        frame.IgnoredBars.ShouldBeGreaterThan(0, "the series really had repeated and older bars");
    }
}
