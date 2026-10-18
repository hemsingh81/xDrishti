using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// ISS-13, adversarial, property: the frame against an independent record, at every step and every lag. The record is the existing bucket reference (<see cref="FrameReference"/>) and the batch indicator reference
/// (<see cref="ReferenceIndicators"/>) over the closed buckets in closing order. The rule is written here from the definition: with D the India date of the minute just fed and S the number of trailing closed buckets whose
/// start date is D, a session feature reads <c>reference[n − 1 − k]</c> when <c>k &lt; S</c> and is NaN and not ready otherwise (S = 0: pending at every lag, lag 0 included); every other feature reads
/// <c>reference[n − 1 − k]</c> whenever that bucket exists. The specs are shuffled by the seed, an alias of each session feature with a source the indicator ignores shares its instance, the series have gaps, short
/// sessions, a Saturday session and a Muhurat evening, with and without a calendar.
/// </summary>
public sealed class SessionHistoryReferenceAdversarialTests
{
    public static TheoryData<Timeframe, int, bool> Cases
    {
        get
        {
            var data = new TheoryData<Timeframe, int, bool>();
            foreach (var timeframe in AdversarialFrames.Intraday)
            {
                foreach (var seed in new[] { 3, 11, 29 })
                {
                    foreach (var calendar in new[] { false, true })
                    {
                        data.Add(timeframe, seed, calendar);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_lag_at_every_step_is_the_reference_close_of_the_current_date_for_session_features_and_the_positional_close_for_all_others(Timeframe timeframe, int seed, bool useCalendar)
    {
        var depth = seed switch { 3 => 2, 11 => 8, _ => 256 };
        var bars = SeriesGenerator.Random(seed, 700, 1_900, specialSessions: true);
        var random = new Random(seed);
        var specs = TestSpecs.Variants(timeframe).OrderBy(_ => random.Next()).ToList();

        AdversarialReferenceCheck.Run(specs, bars, depth, useCalendar, $"{timeframe.ToCode()} seed {seed} calendar {useCalendar}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void On_sparse_sessions_with_late_and_double_closes_every_lag_is_still_the_reference_close_of_the_current_date(Timeframe timeframe, int seed, bool useCalendar)
    {
        // Thin: two to twelve bars a day at random minutes, 1 day in 12 whole, a quarter of the days not ending at the 15:29 close, holidays, Saturday and Muhurat sessions, the first day mid-way. Late buckets (closed by the next
        // date's first minute) and minutes that close two buckets are the rule here, not the exception.
        var depth = seed switch { 3 => 1, 11 => 4, _ => 256 };
        var series = SeriesGenerator.Thin(seed, 260);
        var random = new Random(seed);
        var specs = TestSpecs.Variants(timeframe).OrderBy(_ => random.Next()).ToList();

        AdversarialReferenceCheck.Run(specs, series.Bars, depth, useCalendar, $"sparse {timeframe.ToCode()} seed {seed} calendar {useCalendar}");
    }

    [Theory]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.H1)]
    public void The_sparse_series_really_holds_late_closes_double_closes_inside_one_date_and_double_closes_across_two_dates(Timeframe timeframe)
    {
        var late = 0;
        var doubleSameDate = 0;
        var doubleAcrossDates = 0;
        foreach (var seed in new[] { 3, 11, 29 })
        {
            // the very series of the test above (calendar off)
            var bars = SeriesGenerator.Thin(seed, 260).Bars;
            var byStep = FrameReference.ClosedByStep(bars, timeframe);
            for (var t = 0; t < bars.Count; t++)
            {
                var date = SessionClock.DateOf(bars[t].EpochSeconds);
                late += byStep[t].Count(b => SessionClock.DateOf(b.Start) != date);
                if (byStep[t].Count == 2)
                {
                    var dates = byStep[t].Select(b => SessionClock.DateOf(b.Start)).ToList();
                    if (dates[0] == dates[1])
                    {
                        doubleSameDate++;
                    }
                    else
                    {
                        doubleAcrossDates++;
                    }
                }
            }
        }

        late.ShouldBeGreaterThan(0, "buckets closed by a later date");
        doubleSameDate.ShouldBeGreaterThan(0, "a minute that closes a late bucket of its own date and its own bucket");
        doubleAcrossDates.ShouldBeGreaterThan(0, "a first minute of a date that closes the late bucket of the date before and its own bucket");
    }

    private static readonly string[] DayLaneNames = ["today_open", "today_high", "prev_day_close", "gap_pct", "prev_day_volume"];

    public static TheoryData<int, bool> AllLanesAtOnce
    {
        get
        {
            var data = new TheoryData<int, bool>();
            foreach (var seed in new[] { 2, 8, 13, 21 })
            {
                data.Add(seed, false);
                data.Add(seed, true);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllLanesAtOnce))]
    public void Six_intraday_lanes_and_the_day_lane_in_one_frame_each_follow_their_own_session_rule(int seed, bool useCalendar)
    {
        // the twelve names on 1m, 5m, 10m, 15m, 30m, 1h (one-bar and two-bar opening ranges), a continuous ema and raw close on each, and the session names the 1D lane supports (which must not restart)
        var bars = SeriesGenerator.Random(seed, 900, 2_000, specialSessions: true);
        var specs = new List<FeatureSpec>();
        foreach (var timeframe in AdversarialFrames.Intraday)
        {
            specs.AddRange(AdversarialFrames.TwelveOn(timeframe));
            specs.Add(AdversarialFrames.TwelveSpec("or_high", timeframe, 2 * Math.Max(1, timeframe.Minutes())));
            specs.Add(AdversarialFrames.Spec("ema", timeframe, 3));
            specs.Add(AdversarialFrames.Spec("close", timeframe));
        }

        specs.AddRange(DayLaneNames.Select(n => AdversarialFrames.Spec(n, Timeframe.D1)));
        specs.Add(AdversarialFrames.Spec("close", Timeframe.D1));
        var random = new Random(seed);
        specs = [.. specs.OrderBy(_ => random.Next())];

        AdversarialReferenceCheck.Run(specs, bars, 8, useCalendar, $"all lanes seed {seed} calendar {useCalendar}");
    }
}
