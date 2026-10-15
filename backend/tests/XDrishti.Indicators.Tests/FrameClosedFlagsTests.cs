using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>BarClosed, ClosedBarsAt and TryLastClosed against the independent grouping reference (criterion 1 of P3-01b).</summary>
public sealed class FrameClosedFlagsTests
{
    private static readonly Timeframe[] Asked = [Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1];

    public static TheoryData<string, bool> Fixtures
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var name in BarFixtures.Names)
            {
                data.Add(name, false);
                data.Add(name, true);
            }

            return data;
        }
    }

    public static TheoryData<int, bool> Seeds
    {
        get
        {
            var data = new TheoryData<int, bool>();
            foreach (var seed in Enumerable.Range(1, 30))
            {
                data.Add(seed, seed % 2 == 0);
            }

            return data;
        }
    }

    private static FeatureFrame Frame(bool calendar, List<Bar> bars, params Timeframe[] timeframes) =>
        FeatureFrame.Create([.. timeframes.Select(t => new FeatureSpec("sma", t, PriceSource.Close, 1))], calendar ? new FrameOptions(CalendarFixtures.For(bars)) : null).Value;

    private static void ShouldAgree(List<Bar> bars, bool calendar, string label)
    {
        var frame = Frame(calendar, bars, Asked);
        var sessions = calendar ? CalendarFixtures.Windows(bars) : null;
        var expected = Asked.ToDictionary(t => t, t => FrameReference.ClosedByStep(bars, t, sessions));
        var last = Asked.ToDictionary(t => t, _ => (ReferenceBucket?)null);

        foreach (var timeframe in Asked)
        {
            frame.BarClosed(timeframe).ShouldBeFalse(label + ": nothing closed before the first step");
            frame.ClosedBarsAt(timeframe).ShouldBe(0);
            frame.TryLastClosed(timeframe, out _).ShouldBeFalse();
        }

        for (var t = 0; t < bars.Count; t++)
        {
            frame.Step(bars[t]);
            foreach (var timeframe in Asked)
            {
                var closed = expected[timeframe][t];
                if (closed.Count > 0)
                {
                    last[timeframe] = closed[^1];
                }

                var where = $"{label}, {timeframe.ToCode()}, step {t}";
                frame.ClosedBarsAt(timeframe).ShouldBe(closed.Count, where);
                frame.BarClosed(timeframe).ShouldBe(closed.Count > 0, where);
                var any = frame.TryLastClosed(timeframe, out var bar);
                any.ShouldBe(last[timeframe] is not null, where);
                if (last[timeframe] is { } want)
                {
                    bar.StartEpochSeconds.ShouldBe(want.Start, where);
                    bar.Bar.ShouldBe(want.AsBar, where);
                    bar.Bars.ShouldBe(want.Bars, where);
                    bar.Complete.ShouldBe(want.Complete, where);
                    bar.Timeframe.ShouldBe(timeframe, where);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_flags_and_the_last_closed_bar_match_the_reference_on_every_fixture(string fixture, bool calendar) =>
        ShouldAgree(BarFixtures.Get(fixture), calendar, fixture + (calendar ? " with calendar" : string.Empty));

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_flags_and_the_last_closed_bar_match_the_reference_on_random_series_with_special_sessions(int seed, bool calendar) =>
        ShouldAgree(SeriesGenerator.Random(seed, 600, 2_500, specialSessions: true), calendar, $"seed {seed}" + (calendar ? " with calendar" : string.Empty));

    [Fact]
    public void One_minute_closes_a_bar_at_every_accepted_step()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3)]).Value;

        foreach (var bar in Minutes.Closes(BarFixtures.Mon, 1, 2, 3, 4))
        {
            frame.Step(bar);
            frame.BarClosed(Timeframe.M1).ShouldBeTrue();
            frame.ClosedBarsAt(Timeframe.M1).ShouldBe(1);
            frame.TryLastClosed(Timeframe.M1, out var last).ShouldBeTrue();
            (last.Bar, last.Bars, last.Complete, last.Timeframe).ShouldBe((bar, 1, true, Timeframe.M1));
        }
    }

    [Fact]
    public void A_minute_that_closes_two_buckets_reports_two_and_the_second_is_the_last_closed()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 1)]).Value;
        frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.Open, 10, 12, 9, 11, 5));

        // 09:24 is the only bar of the 09:20 bucket and its last minute: it also proves the 09:15 bucket is over
        frame.Step(Minutes.Bar(BarFixtures.Mon, Minutes.Open + 9, 20, 22, 19, 21, 7));

        frame.ClosedBarsAt(Timeframe.M5).ShouldBe(2);
        frame.BarClosed(Timeframe.M5).ShouldBeTrue();
        frame.TryLastClosed(Timeframe.M5, out var last).ShouldBeTrue();
        last.StartEpochSeconds.ShouldBe(Minutes.Epoch(BarFixtures.Mon, Minutes.Open + 5));
        (last.Bars, last.Complete).ShouldBe((1, false));
        frame.IncompleteBarsConsumed.ShouldBe(2);
    }

    [Fact]
    public void A_late_closed_incomplete_bucket_is_reported_by_the_bar_that_proves_it_over_with_the_incomplete_flag()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 1)]).Value;
        for (var m = Minutes.Open; m < Minutes.Open + 4; m++)
        {
            frame.Step(Minutes.Close(BarFixtures.Mon, m, m));
            frame.BarClosed(Timeframe.M5).ShouldBeFalse();
        }

        // 09:19 never comes; 09:20 starts the next bucket and closes the previous one
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 5, 1));

        frame.BarClosed(Timeframe.M5).ShouldBeTrue();
        frame.TryLastClosed(Timeframe.M5, out var last).ShouldBeTrue();
        (last.Bars, last.Complete).ShouldBe((4, false));
        frame.Step(Minutes.Close(BarFixtures.Mon, Minutes.Open + 6, 1));
        frame.BarClosed(Timeframe.M5).ShouldBeFalse();
        frame.TryLastClosed(Timeframe.M5, out var still).ShouldBeTrue();
        still.ShouldBe(last, "the last closed bar stays until the next one closes");
    }

    [Fact]
    public void A_special_session_closes_its_day_at_its_own_last_minute_only_when_the_frame_has_the_calendar()
    {
        var bars = BarFixtures.Get("saturday session");
        var withCalendar = Frame(true, bars, Timeframe.D1);
        var without = Frame(false, bars, Timeframe.D1);

        foreach (var bar in bars)
        {
            withCalendar.Step(bar);
            without.Step(bar);
        }

        withCalendar.BarClosed(Timeframe.D1).ShouldBeTrue();
        without.BarClosed(Timeframe.D1).ShouldBeFalse();
        without.TryLastClosed(Timeframe.D1, out _).ShouldBeFalse();
        withCalendar.TryLastClosed(Timeframe.D1, out var day).ShouldBeTrue();
        (day.Bars, day.Complete).ShouldBe((30, false));

        // the first bar of a later session closes it for the frame without the calendar
        without.Step(BarFixtures.Walk(new DateOnly(2026, 3, 9), Minutes.Open, 1)[0]);
        without.BarClosed(Timeframe.D1).ShouldBeTrue();
    }

    [Fact]
    public void Asking_for_a_timeframe_the_frame_was_not_built_for_is_an_error_not_a_silent_false()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 3), new FeatureSpec("sma", Timeframe.D1, PriceSource.Close, 3)]).Value;

        frame.HasTimeframe(Timeframe.M5).ShouldBeTrue();
        frame.HasTimeframe(Timeframe.D1).ShouldBeTrue();
        foreach (var timeframe in new[] { Timeframe.M1, Timeframe.M15, Timeframe.H1, Timeframe.W1, (Timeframe)99 })
        {
            frame.HasTimeframe(timeframe).ShouldBeFalse();
            Should.Throw<ArgumentOutOfRangeException>(() => frame.BarClosed(timeframe));
            Should.Throw<ArgumentOutOfRangeException>(() => frame.ClosedBarsAt(timeframe));
            Should.Throw<ArgumentOutOfRangeException>(() => frame.TryLastClosed(timeframe, out _));
        }
    }

    [Fact]
    public void A_bar_the_ordering_guard_skips_closes_nothing_on_any_timeframe()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 2), new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 2)]).Value;
        var bars = Minutes.Closes(BarFixtures.Mon, 1, 2, 3, 4, 5);
        foreach (var bar in bars)
        {
            frame.Step(bar);
        }

        frame.BarClosed(Timeframe.M5).ShouldBeTrue();

        frame.Step(bars[^1]);

        frame.BarClosed(Timeframe.M1).ShouldBeFalse();
        frame.BarClosed(Timeframe.M5).ShouldBeFalse();
        frame.ClosedBarsAt(Timeframe.M1).ShouldBe(0);
        frame.IgnoredBars.ShouldBe(1);
    }

    [Fact]
    public void Reset_forgets_the_flags_and_the_last_closed_bar()
    {
        var frame = Frame(false, [], Timeframe.M1, Timeframe.M5);
        foreach (var bar in BarFixtures.Get("normal day").Take(10))
        {
            frame.Step(bar);
        }

        frame.Reset();

        frame.BarClosed(Timeframe.M1).ShouldBeFalse();
        frame.BarClosed(Timeframe.M5).ShouldBeFalse();
        frame.TryLastClosed(Timeframe.M5, out _).ShouldBeFalse();
        frame.IgnoredBars.ShouldBe(0);
    }
}
