using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// The cold-start property of the per-session features (ADR 0035), checked at every step of long seeded series and written from the definition, with no clearing or pending logic of its own: on an intraday timeframe
/// a restarting feature of the long-running frame reads, at every lag, <b>as if the frame had been created at the first accepted bar of the current India date</b> (<c>vwap</c>, <c>or_*</c>, <c>today_*</c>: a fresh
/// frame fed that date's bars only), and for <c>prev_day_*</c> and <c>gap_pct</c> as if it had been created at the first bar of the previous date that had bars (a fresh frame fed that date and the current one; the
/// lags that reach back before the lane's first bar of the current date are unknown by definition, and the number of bars the lane has closed in the date is counted by the fresh frame of the date). The dates are
/// split with <see cref="SessionClock.DateOf"/> of the Domain, the ordering guard is written again here, and nothing is taken from <c>IndicatorInfo</c>.
/// </summary>
public sealed class FrameColdStartTests
{
    private static readonly Timeframe[] Intraday = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1];

    /// <summary>The nine names whose value of a session depends on that session's bars only (written out, not read from the catalog).</summary>
    private static readonly string[] SessionLocal = ["vwap", "or_high", "or_low", "today_open", "today_high", "today_low"];

    /// <summary>The names that need the session before as well.</summary>
    private static readonly string[] NeedsPrevious = ["prev_day_open", "prev_day_high", "prev_day_low", "prev_day_close", "prev_day_volume", "gap_pct"];

    public static TheoryData<string, int, int, bool> Cases
    {
        get
        {
            var data = new TheoryData<string, int, int, bool>();
            foreach (var kind in new[] { "dense", "gappy", "sparse" })
            {
                foreach (var seed in new[] { 1, 2, 3 })
                {
                    foreach (var depth in new[] { 2, 8 })
                    {
                        foreach (var calendar in new[] { false, true })
                        {
                            data.Add(kind, seed, depth, calendar);
                        }
                    }
                }

                foreach (var calendar in new[] { false, true })
                {
                    data.Add(kind, 7, 256, calendar);
                }
            }

            return data;
        }
    }

    /// <summary>The restarting specs of every intraday timeframe and, per timeframe, a plain <c>close</c> (so the frame has a lane even for a timeframe whose restarting specs are few).</summary>
    private static List<FeatureSpec> Specs()
    {
        var specs = new List<FeatureSpec>();
        foreach (var timeframe in Intraday)
        {
            specs.AddRange(TestSpecs.Variants(timeframe).Where(s => SessionLocal.Contains(s.Name) || NeedsPrevious.Contains(s.Name)));
            specs.Add(new FeatureSpec("close", timeframe));
        }

        return specs;
    }

    private static List<Bar> Series(string kind, int seed, int depth) => kind switch
    {
        // a few bars at random minutes per date, holidays, a Saturday now and then, a Muhurat evening: double closes, late closes and pending lanes are frequent
        "sparse" => SeriesGenerator.Thin(seed, depth == 256 ? 150 : 220).Bars,
        "gappy" => Gappy(SeriesGenerator.Random(seed, depth == 256 ? 500 : 600, depth == 256 ? 900 : 2_000, specialSessions: true), seed),
        _ => SeriesGenerator.Random(seed, depth == 256 ? 500 : 600, depth == 256 ? 900 : 2_000, specialSessions: true),
    };

    /// <summary>The clean series with the same minute again or an older bar replayed after 8 bars in 100 (other prices, so a leak would show).</summary>
    private static List<Bar> Gappy(List<Bar> clean, int seed)
    {
        var random = new Random(seed * 31);
        var dirty = new List<Bar>(clean.Count + clean.Count / 10);
        for (var i = 0; i < clean.Count; i++)
        {
            dirty.Add(clean[i]);
            if (random.NextDouble() < 0.08)
            {
                var older = random.NextDouble() < 0.5 ? clean[i] : clean[random.Next(0, i + 1)];
                dirty.Add(older with { Close = older.Close + 7, High = older.High + 9, Volume = older.Volume + 1_000 });
            }
        }

        return dirty;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_restarting_feature_of_the_long_frame_reads_as_if_the_frame_had_been_created_at_the_start_of_the_session(string kind, int seed, int depth, bool withCalendar)
    {
        var bars = Series(kind, seed, depth);
        var calendar = withCalendar ? CalendarFixtures.For(bars) : null;
        var specs = Specs();
        var options = new FrameOptions(calendar, depth);
        var frame = FeatureFrame.Create(specs, options).Value;
        var today = FeatureFrame.Create(specs, options).Value;
        var twoDays = FeatureFrame.Create(specs, options).Value;
        var timeframeIndex = specs.Select(s => Array.IndexOf(Intraday, s.Timeframe)).ToArray();
        var local = specs.Select(s => SessionLocal.Contains(s.Name)).ToArray();
        var restarting = specs.Select(s => SessionLocal.Contains(s.Name) || NeedsPrevious.Contains(s.Name)).ToArray();

        // the ordering guard, written again: a bar is accepted when it is later than the last accepted one; the accepted bars of the current date and of the date before it that had bars
        var anyAccepted = false;
        var lastAccepted = 0L;
        DateOnly? currentDate = null;
        var currentBars = new List<Bar>();
        var previousBars = new List<Bar>();
        var closedToday = new int[Intraday.Length];
        var compared = 0L;
        var unknown = 0L;
        var dateChanges = 0;

        for (var t = 0; t < bars.Count; t++)
        {
            var bar = bars[t];
            frame.Step(bar);
            if (!anyAccepted || bar.EpochSeconds > lastAccepted)
            {
                anyAccepted = true;
                lastAccepted = bar.EpochSeconds;
                var date = SessionClock.DateOf(bar.EpochSeconds);
                if (currentDate != date)
                {
                    dateChanges++;
                    previousBars = currentBars;
                    currentBars = [];
                    currentDate = date;
                    today.Reset();
                    twoDays.Reset();
                    Array.Clear(closedToday);
                    foreach (var earlier in previousBars)
                    {
                        twoDays.Step(earlier);
                    }
                }

                currentBars.Add(bar);
                today.Step(bar);
                twoDays.Step(bar);
                for (var i = 0; i < Intraday.Length; i++)
                {
                    closedToday[i] += today.ClosedBarsAt(Intraday[i]);
                }
            }

            for (var h = 0; h < specs.Count; h++)
            {
                if (!restarting[h])
                {
                    continue;
                }

                var closed = closedToday[timeframeIndex[h]];
                for (var lag = 0; lag < depth; lag++)
                {
                    // the session-local names: what a frame that has seen this date only says; the others: the same, from the frame that saw the date before it too, but nothing before the lane's first bar of the date
                    var want = local[h] ? today.At(h, lag) : lag < closed ? twoDays.At(h, lag) : double.NaN;
                    var got = frame.At(h, lag);
                    compared++;
                    if (double.IsNaN(want))
                    {
                        unknown++;
                    }

                    if (!Tolerance.Identical(want, got) || frame.IsReadyAt(h, lag) == double.IsNaN(want))
                    {
                        Assert.Fail($"{kind} seed {seed}, depth {depth}, calendar {withCalendar}, step {t}, {TestSpecs.Describe(specs[h])}, lag {lag}: the cold-start frame has {want:R}, the long frame {got:R} (ready {frame.IsReadyAt(h, lag)}; the lane closed {closed} bars of the date)");
                    }
                }
            }
        }

        // the property is not vacuous: several dates, plenty of comparisons, and a good share of them unknown by the rule
        dateChanges.ShouldBeGreaterThan(2);
        compared.ShouldBeGreaterThan(10_000);
        unknown.ShouldBeGreaterThan(compared / 100);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_property_can_fail_the_positional_reading_of_the_old_frame_disagrees_with_it_on_every_timeframe(int seed)
    {
        // the control: a reading that never restarts (the reference indicator over all the closed bars of the timeframe, as the frame was read before ADR 0035) differs from the long frame in many places
        var bars = SeriesGenerator.Random(seed, 1_500, 2_000, specialSessions: true);
        var depth = 8;
        var specs = Specs().Where(s => SessionLocal.Contains(s.Name) || NeedsPrevious.Contains(s.Name)).ToList();
        var frame = FeatureFrame.Create(specs, new FrameOptions(HistoryDepth: depth)).Value;
        var closedByStep = Intraday.ToDictionary(t => t, t => FrameReference.ClosedByStep(bars, t));
        var positional = specs.Select(s => ReferenceIndicators.Compute(s, [.. closedByStep[s.Timeframe].SelectMany(x => x).Select(b => b.AsBar)])).ToList();
        var closedSoFar = new int[specs.Count];
        var differences = Intraday.ToDictionary(t => t, _ => 0);

        for (var t = 0; t < bars.Count; t++)
        {
            frame.Step(bars[t]);
            for (var h = 0; h < specs.Count; h++)
            {
                closedSoFar[h] += closedByStep[specs[h].Timeframe][t].Count;
                for (var lag = 0; lag < depth; lag++)
                {
                    var index = closedSoFar[h] - 1 - lag;
                    var run = index < 0 ? double.NaN : positional[h][index];
                    if (!Tolerance.Same(run, frame.At(h, lag)))
                    {
                        differences[specs[h].Timeframe]++;
                    }
                }
            }
        }

        foreach (var timeframe in Intraday)
        {
            differences[timeframe].ShouldBeGreaterThan(MinimumDifferences(timeframe), $"seed {seed}, {timeframe.ToCode()}: places where the positional reading and the frame disagree");
        }
    }

    /// <summary>
    /// The control must disagree in at least this many (step, feature, lag) places on the timeframe. Measured on the two seeds above (about 1,500 to 2,000 one-minute bars, 8 lags, 16 to 18 restarting specs per timeframe):
    /// 1m 1,624 and 2,346; 5m 9,754 and 13,868; 10m 19,966 and 26,452; 15m 26,052 and 33,118; 30m 52,538 and 60,328; 1h 91,948 and 104,360. The bound is about half of the smaller figure.
    /// </summary>
    private static int MinimumDifferences(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => 800,
        Timeframe.M5 => 4_800,
        Timeframe.M10 => 10_000,
        Timeframe.M15 => 13_000,
        Timeframe.M30 => 26_000,
        _ => 45_000,
    };
}
