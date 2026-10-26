using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// What the frame's date lane gives for the ten market-context features (P3-01c criteria 9 and 10): the value at a step is that of the latest context day <b>strictly before</b> the India date of the step, it holds for
/// the whole date, its history has one entry per date, a missing VIX does not stop the regime, and a frame reads a context it shares with others without changing it. The expected values come from the independent
/// batch reference (<see cref="ReferenceContext"/>) and from plain scans of the days, not from the library's own search.
/// </summary>
public sealed class FrameContextSemanticsTests
{
    private static readonly IReadOnlyList<string> MarketNames = TestSpecs.MarketContextNames;

    private static readonly DateOnly Monday = new(2026, 3, 2);

    /// <summary>All twelve names: the two calendar ones first, then the ten that read the context.</summary>
    private static FeatureFrame AllNamesFrame(MarketContext context, IReadOnlySet<DateOnly>? expiry = null) =>
        FrameContextFixtures.Frame(TestSpecs.ContextNames, context, expiry ?? new HashSet<DateOnly>());

    // ---- against the independent reference -------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Seeds))]
    public void At_every_step_each_feature_equals_the_batch_reference_of_the_latest_day_before_the_date(int seed)
    {
        var days = ContextSeriesGenerator.Random(seed, 300, 700);
        var options = seed % 2 == 0 ? new ContextOptions() : FrameContextFixtures.Small;
        var reference = ReferenceContext.Compute(days, options);
        var columns = MarketNames.Select(name => FrameContextFixtures.Column(reference, name)).ToList();
        var random = new Random(seed);
        var startIndex = seed % 3 == 0 ? 0 : random.Next(0, days.Count);
        var first = seed % 3 == 0 ? days[0].Date.AddDays(-4) : days[startIndex].Date.AddDays(-random.Next(0, 3));
        var last = days[^1].Date.AddDays(6);
        var frame = FrameContextFixtures.Frame(MarketNames, FrameContextFixtures.Build(days, options));
        var ready = new int[MarketNames.Count];

        foreach (var date in FrameContextFixtures.Dates(first, last))
        {
            var index = FrameContextFixtures.LatestBefore(days, date);
            foreach (var bar in FrameContextFixtures.OnDates([date], Minutes.Open, Minutes.At(12, 0), Minutes.At(15, 29)))
            {
                frame.Step(bar);

                for (var h = 0; h < MarketNames.Count; h++)
                {
                    var expected = index < 0 ? double.NaN : columns[h][index];
                    var same = Tolerance.Same(expected, frame[h]) && frame.IsReady(h) == !double.IsNaN(expected);
                    if (!same)
                    {
                        same.ShouldBeTrue($"seed {seed} {MarketNames[h]} on {date:yyyy-MM-dd}: expected {expected:R}, got {frame[h]:R}, ready {frame.IsReady(h)}");
                    }

                    ready[h] += frame.IsReady(h) ? 1 : 0;
                }
            }
        }

        // the comparison above must not be vacuous: the index ADX (handle 1) and the regime (handle 9) have values at the end of every context, so the frame had them at some step
        ready[1].ShouldBeGreaterThan(0, $"seed {seed}: index_adx was ready at some step");
        ready[9].ShouldBeGreaterThan(0, $"seed {seed}: regime was ready at some step");
    }

    public static TheoryData<int> Seeds() => [.. Enumerable.Range(0, 24)];

    [Fact]
    public void A_hand_calculated_context_of_three_days_is_read_one_day_late()
    {
        var days = new List<ContextDay>
        {
            new(Monday, 101, 99, 100, 15, 40),
            new(Monday.AddDays(1), 102, 100, 101, 18, 55),
            new(Monday.AddDays(2), 103, 101, 102, 21, 70),
        };
        var frame = FrameContextFixtures.Frame(["vix", "vix_chg", "breadth", "index_trend", "regime"], FrameContextFixtures.Build(days));
        var change18 = (18.0 - 15.0) / 15.0 * 100.0;
        var change21 = (21.0 - 18.0) / 18.0 * 100.0;

        // date, vix, vix_chg, breadth: Monday sees nothing (the first day is Monday's own), Tuesday sees Monday, ..., Friday and the next Monday still see Wednesday
        var expectations = new (DateOnly Date, double Vix, double VixChg, double Breadth)[]
        {
            (Monday, double.NaN, double.NaN, double.NaN),
            (Monday.AddDays(1), 15, double.NaN, 40),
            (Monday.AddDays(2), 18, change18, 55),
            (Monday.AddDays(3), 21, change21, 70),
            (Monday.AddDays(4), 21, change21, 70),
            (Monday.AddDays(7), 21, change21, 70),
        };
        foreach (var (date, vix, vixChg, breadth) in expectations)
        {
            foreach (var bar in FrameContextFixtures.OnDates([date], Minutes.Open, Minutes.At(15, 29)))
            {
                frame.Step(bar);

                Tolerance.Same(vix, frame[0]).ShouldBeTrue($"vix on {date:yyyy-MM-dd}");
                Tolerance.Same(vixChg, frame[1]).ShouldBeTrue($"vix_chg on {date:yyyy-MM-dd}");
                Tolerance.Same(breadth, frame[2]).ShouldBeTrue($"breadth on {date:yyyy-MM-dd}");
                frame.IsReady(3).ShouldBeFalse("three days are far too few for the trend");
                frame.IsReady(4).ShouldBeFalse();
            }
        }

        // the history has one entry per date stepped (Mon, Tue, Wed, Thu, Fri, Mon): lag 0 is the second Monday, 1 the Friday, 2 the Thursday, 3 the Wednesday, 4 the Tuesday, 5 the first Monday
        frame.At(0, 0).ShouldBe(21.0);
        frame.At(0, 1).ShouldBe(21.0, "Friday's");
        frame.At(0, 2).ShouldBe(21.0, "Thursday's");
        frame.At(0, 3).ShouldBe(18.0, "Wednesday's");
        frame.At(0, 4).ShouldBe(15.0, "Tuesday's");
        frame.IsReadyAt(0, 5).ShouldBeFalse("the first Monday saw nothing");
        frame.IsReadyAt(0, 6).ShouldBeFalse("before the first step");
    }

    // ---- criterion 9: no look-ahead ----------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_day_record_dated_d_is_in_the_context_and_the_frame_at_every_minute_of_d_sees_the_day_before(int seed)
    {
        // VIX and breadth differ on every day, so a value of the wrong day is noticed
        var days = FrameContextFixtures.Consecutive(seed, 70).Select((d, i) => d with { VixClose = 20 + 1.25 * i, Breadth = i * 3 % 100 }).ToList();
        var context = FrameContextFixtures.Build(days, FrameContextFixtures.Small);
        var own = ContextRead.Days(context, days);
        var frame = FrameContextFixtures.Frame(MarketNames, context);

        for (var i = 0; i < days.Count; i++)
        {
            var steps = 0;
            foreach (var bar in FrameContextFixtures.Session(days[i].Date))
            {
                frame.Step(bar);
                steps++;

                for (var h = 0; h < MarketNames.Count; h++)
                {
                    var expected = i == 0 ? double.NaN : ContextRead.Fields(own[i - 1])[h];
                    Tolerance.Identical(expected, frame[h]).ShouldBeTrue($"{MarketNames[h]} at minute {steps} of {days[i].Date:yyyy-MM-dd}: expected the value of day {i - 1}");
                }

                if (i > 0)
                {
                    frame[3].ShouldNotBe(days[i].VixClose!.Value, $"the VIX of {days[i].Date:yyyy-MM-dd} is not read on that date");
                    frame[6].ShouldNotBe(days[i].Breadth!.Value, "nor is its breadth");
                }
            }

            steps.ShouldBe(375);
        }

        // the day after the last record sees the last record, and the record that was present all along has not changed
        frame.Step(Minutes.Close(days[^1].Date.AddDays(1), Minutes.Open, 100));
        frame[3].ShouldBe(days[^1].VixClose!.Value);
        frame[6].ShouldBe(days[^1].Breadth!.Value);
        context.Count.ShouldBe(days.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void The_values_at_the_open_and_at_the_last_minute_of_a_date_are_the_same_and_so_are_all_the_minutes_between(int seed)
    {
        var bars = SeriesGenerator.Random(seed, 5_000, 9_000, specialSessions: true);
        var days = FrameContextFixtures.Covering(bars, seed);
        var frame = AllNamesFrame(FrameContextFixtures.Build(days));
        var firstOfDate = new List<long>();
        var current = default(DateOnly);
        var datesWithSeveralSteps = 0;
        var steps = 0;
        var readyOnSomeDate = false;

        foreach (var bar in bars)
        {
            frame.Step(bar);

            var date = FrameContextFixtures.DateOf(bar);
            var bits = Enumerable.Range(0, frame.Count).Select(h => BitConverter.DoubleToInt64Bits(frame[h])).ToList();
            if (date != current)
            {
                current = date;
                steps = 0;
                firstOfDate = bits;
            }

            steps++;
            datesWithSeveralSteps += steps == 2 ? 1 : 0;
            readyOnSomeDate |= frame.IsReady(MarketNames.Count + 1);
            for (var h = 0; h < frame.Count; h++)
            {
                if (bits[h] != firstOfDate[h])
                {
                    bits[h].ShouldBe(firstOfDate[h], $"seed {seed} {TestSpecs.ContextNames[h]} on {date:yyyy-MM-dd}, step {steps} of the date");
                }
            }
        }

        datesWithSeveralSteps.ShouldBeGreaterThan(10);
        readyOnSomeDate.ShouldBeTrue("the regime was ready on some date, with a context that started 420 days before");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_history_has_one_entry_per_date_so_that_lag_k_is_the_value_k_dates_ago(int seed)
    {
        var bars = SeriesGenerator.Random(seed, 6_000, 9_000, specialSessions: true);
        var days = FrameContextFixtures.Covering(bars, seed);
        var reference = ReferenceContext.Compute(days, new ContextOptions());
        var frame = AllNamesFrame(FrameContextFixtures.Build(days));
        var perDate = new List<double[]>();
        var current = default(DateOnly);
        var checkedLags = 0;

        foreach (var bar in bars)
        {
            var date = FrameContextFixtures.DateOf(bar);
            if (perDate.Count == 0 || date != current)
            {
                // the value of each feature on this date, from the plain scan and the batch reference (the two calendar names from the platform's calendar)
                var index = FrameContextFixtures.LatestBefore(days, date);
                current = date;
                perDate.Add(
                [
                    FrameContextFixtures.IsoWeekday(date),
                    0,
                    .. MarketNames.Select(name => index < 0 ? double.NaN : FrameContextFixtures.Column(reference, name)[index]),
                ]);
            }

            frame.Step(bar);

            for (var h = 0; h < frame.Count; h++)
            {
                for (var lag = 0; lag < frame.HistoryDepth; lag++)
                {
                    var expected = lag < perDate.Count ? perDate[^(lag + 1)][h] : double.NaN;
                    var same = Tolerance.Same(expected, frame.At(h, lag)) && frame.IsReadyAt(h, lag) == !double.IsNaN(expected);
                    if (!same)
                    {
                        same.ShouldBeTrue($"seed {seed} {TestSpecs.ContextNames[h]} lag {lag} on {date:yyyy-MM-dd}: expected {expected:R}, got {frame.At(h, lag):R}");
                    }

                    checkedLags++;
                }
            }
        }

        perDate.Count.ShouldBeGreaterThan(10);
        checkedLags.ShouldBeGreaterThan(100_000);
    }

    // ---- criterion 10: not ready, a missing VIX, reset, sharing ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_that_reads_the_context_is_ready_until_the_context_has_a_day_before_the_date_and_the_calendar_features_are_ready_at_once()
    {
        var days = FrameContextFixtures.Consecutive(8, 30, Monday);
        var frame = AllNamesFrame(FrameContextFixtures.Build(days, FrameContextFixtures.Small));

        foreach (var date in new[] { Monday.AddDays(-10), Monday.AddDays(-1), Monday })
        {
            frame.Step(Minutes.Close(date, Minutes.Open, 100));

            frame.IsReady(0).ShouldBeTrue("day_of_week");
            frame.IsReady(1).ShouldBeTrue("is_expiry_day");
            for (var h = 2; h < frame.Count; h++)
            {
                frame.IsReady(h).ShouldBeFalse($"{TestSpecs.ContextNames[h]} on {date:yyyy-MM-dd}: no context day is before it");
                frame[h].ShouldBeNaN();
            }
        }

        frame.Step(Minutes.Close(Monday.AddDays(1), Minutes.Open, 100));

        frame.IsReady(frame.HandleOf(new FeatureSpec("vix"))).ShouldBeTrue("the first day is visible the next date");
        frame.IsReady(frame.HandleOf(new FeatureSpec("breadth"))).ShouldBeTrue();
        frame.IsReady(frame.HandleOf(new FeatureSpec("index_trend"))).ShouldBeFalse("but its trend needs more days");
    }

    [Fact]
    public void An_empty_context_never_makes_a_context_feature_ready()
    {
        var frame = AllNamesFrame(FrameContextFixtures.Build([]));

        foreach (var bar in FrameContextFixtures.OnDates(FrameContextFixtures.Dates(Monday, Monday.AddDays(40)), Minutes.Open))
        {
            frame.Step(bar);
        }

        for (var h = 2; h < frame.Count; h++)
        {
            frame.IsReady(h).ShouldBeFalse(TestSpecs.ContextNames[h]);
        }

        frame.IsReady(0).ShouldBeTrue();
    }

    [Fact]
    public void A_day_without_a_vix_stops_the_vix_features_on_that_date_but_not_the_regime()
    {
        var days = FrameContextFixtures.Consecutive(4, 320);
        days[290] = days[290] with { VixClose = null };
        var options = new ContextOptions();
        var reference = ReferenceContext.Compute(days, options);
        var frame = FrameContextFixtures.Frame(MarketNames, FrameContextFixtures.Build(days, options));
        int H(string name) => frame.HandleOf(new FeatureSpec(name));

        // the dates that see day 288, 289, 290 (no VIX), 291 and 292: each is the date of the next record
        for (var k = 288; k <= 292; k++)
        {
            frame.Step(Minutes.Close(days[k + 1].Date, Minutes.Open, 100));

            foreach (var name in MarketNames)
            {
                Tolerance.Same(FrameContextFixtures.Column(reference, name)[k], frame[H(name)]).ShouldBeTrue($"{name} as of day {k}");
                frame.IsReady(H(name)).ShouldBe(!double.IsNaN(FrameContextFixtures.Column(reference, name)[k]), $"{name} as of day {k}");
            }

            var missing = k == 290;
            frame.IsReady(H("vix")).ShouldBe(!missing);
            frame.IsReady(H("vix_pct")).ShouldBe(!missing);
            frame.IsReady(H("vix_chg")).ShouldBe(k is not (290 or 291), "the change needs this day's VIX and the previous day's");
            frame.IsReady(H("regime_vol")).ShouldBeTrue("index-only volatility when the VIX is missing");
            frame.IsReady(H("regime")).ShouldBeTrue();
            frame.IsReady(H("index_vol_pct")).ShouldBeTrue();
        }

        // the history keeps the gap: lag 2 on the date after day 292's record is the value as of day 290
        frame.At(H("vix"), 2).ShouldBeNaN();
        frame.IsReadyAt(H("vix"), 2).ShouldBeFalse();
        frame.IsReadyAt(H("regime"), 2).ShouldBeTrue();
    }

    [Fact]
    public void A_context_without_any_vix_leaves_the_vix_features_never_ready_and_the_regime_ready_when_the_index_part_is()
    {
        var days = FrameContextFixtures.Consecutive(6, 320, vix: false);
        var frame = FrameContextFixtures.Frame(MarketNames, FrameContextFixtures.Build(days));
        var firstRegime = -1;

        // one step on the date of every record: the date of record i sees record i - 1
        for (var i = 0; i < days.Count; i++)
        {
            frame.Step(Minutes.Close(days[i].Date, Minutes.Open, 100));

            foreach (var name in new[] { "vix", "vix_pct", "vix_chg" })
            {
                frame.IsReady(frame.HandleOf(new FeatureSpec(name))).ShouldBeFalse($"{name} on record {i}");
            }

            if (firstRegime < 0 && frame.IsReady(frame.HandleOf(new FeatureSpec("regime"))))
            {
                firstRegime = i;
            }
        }

        firstRegime.ShouldBe(265, "the date of record 265 sees record 264, the 265th day: 14 + 252 - 1");
    }

    [Fact]
    public void Reset_clears_the_lane_and_the_history_and_the_frame_then_repeats_a_fresh_one()
    {
        var bars = SeriesGenerator.Random(5, 6_000, 9_000, specialSessions: true);
        var context = FrameContextFixtures.Build(FrameContextFixtures.Covering(bars, 5));
        var frame = AllNamesFrame(context);
        var fresh = AllNamesFrame(context);

        var first = FrameContextFixtures.Trace(frame, bars);
        frame.Reset();

        frame.Steps.ShouldBe(0);
        for (var h = 0; h < frame.Count; h++)
        {
            frame.IsReady(h).ShouldBeFalse(TestSpecs.ContextNames[h]);
            frame[h].ShouldBeNaN();
            frame.IsReadyAt(h, 1).ShouldBeFalse();
        }

        frame.BarClosed(Timeframe.M1).ShouldBeFalse();
        FrameContextFixtures.Trace(frame, bars).ShouldBe(first);
        FrameContextFixtures.Trace(fresh, bars).ShouldBe(first);
        FrameContextFixtures.Bits(frame).ShouldBe(FrameContextFixtures.Bits(fresh));
    }

    [Fact]
    public void A_reset_frame_that_starts_on_the_same_date_again_refreshes_at_its_first_step()
    {
        var days = FrameContextFixtures.Consecutive(3, 40, Monday);
        var frame = FrameContextFixtures.Frame(["vix", "day_of_week"], FrameContextFixtures.Build(days, FrameContextFixtures.Small));
        var date = days[10].Date;

        frame.Step(Minutes.Close(date, Minutes.Open, 100));
        var before = frame[0];
        frame.Reset();
        frame.IsReady(0).ShouldBeFalse();
        frame.Step(Minutes.Close(date, Minutes.Open, 100));

        frame[0].ShouldBe(before, "the same date read again after Reset is not mistaken for a date already held");
        frame.IsReadyAt(0, 1).ShouldBeFalse("the history was cleared");
    }

    [Fact]
    public void A_reset_frame_reads_the_epoch_day_again_as_its_first_date()
    {
        var frame = FrameContextFixtures.Frame(["day_of_week", "vix"], FrameContextFixtures.Build(FrameContextFixtures.Consecutive(3, 20, Monday)));
        var bar = new Bar(0, 1, 1, 1, 1, 1);

        frame.Step(bar);
        frame[0].ShouldBe(4.0, "1970-01-01 was a Thursday");
        frame.Reset();
        frame.Step(bar);

        frame[0].ShouldBe(4.0, "the first step after Reset is the first step of its date, also when the date is day number 0");
        frame.IsReadyAt(0, 1).ShouldBeFalse("the history was cleared and the date written once");
    }

    [Fact]
    public void Two_frames_that_share_one_context_give_identical_values_and_leave_the_context_as_it_was()
    {
        var bars = SeriesGenerator.Random(9, 6_000, 9_000, specialSessions: true);
        var days = FrameContextFixtures.Covering(bars, 9);
        var context = FrameContextFixtures.Build(days);
        var before = ContextRead.Days(context, days);
        var a = AllNamesFrame(context);
        var b = AllNamesFrame(context);

        var traceA = FrameContextFixtures.Trace(a, bars);
        var traceB = FrameContextFixtures.Trace(b, bars);

        traceB.ShouldBe(traceA);
        context.Count.ShouldBe(days.Count);
        ContextRead.Days(context, days).Zip(before).ShouldAllBe(p => ContextRead.Identical(p.First, p.Second));
    }

    [Fact]
    public void Frames_on_several_threads_that_share_one_context_give_the_same_values_as_a_single_run()
    {
        var bars = SeriesGenerator.Random(12, 6_000, 9_000, specialSessions: true);
        var context = FrameContextFixtures.Build(FrameContextFixtures.Covering(bars, 12));
        var expected = FrameContextFixtures.Trace(AllNamesFrame(context), bars);
        var traces = new List<long>[8];

        Parallel.For(0, traces.Length, worker => traces[worker] = FrameContextFixtures.Trace(AllNamesFrame(context), bars));

        foreach (var trace in traces)
        {
            trace.ShouldBe(expected);
        }
    }

    [Fact]
    public void A_frame_that_starts_in_the_middle_of_the_context_reads_the_same_as_one_that_started_at_its_beginning()
    {
        var days = FrameContextFixtures.Consecutive(10, 400);
        var context = FrameContextFixtures.Build(days);
        var dates = days.Select(d => d.Date).ToList();
        var whole = AllNamesFrame(context);
        var partial = AllNamesFrame(context);

        for (var i = 0; i < dates.Count; i++)
        {
            var bar = Minutes.Close(dates[i], Minutes.Open, 100);
            whole.Step(bar);
            if (i >= 337)
            {
                partial.Step(bar);

                for (var h = 0; h < whole.Count; h++)
                {
                    Tolerance.Identical(whole[h], partial[h]).ShouldBeTrue($"{TestSpecs.ContextNames[h]} on {dates[i]:yyyy-MM-dd}");
                }
            }
        }

        var regime = partial.HandleOf(new FeatureSpec("regime"));
        partial.IsReady(regime).ShouldBeTrue("day 337 and later have a regime");
        partial.IsReadyAt(regime, 1).ShouldBeTrue("two dates in, the history of the partial frame holds a regime");
    }

    [Fact]
    public void A_frame_that_runs_past_the_end_of_the_context_keeps_the_values_of_its_last_day_for_MaxAgeDays_and_then_has_none()
    {
        var days = FrameContextFixtures.Consecutive(11, 60, Monday);
        var context = FrameContextFixtures.Build(days, FrameContextFixtures.Small);
        var own = ContextRead.Days(context, days);
        var frame = FrameContextFixtures.Frame(MarketNames, context);
        var last = days[^1].Date;

        foreach (var date in FrameContextFixtures.Dates(last.AddDays(1), last.AddDays(400)))
        {
            frame.Step(Minutes.Close(date, Minutes.Open, 100));

            if (date.DayNumber - last.DayNumber <= FrameContextFixtures.Small.MaxAgeDays)
            {
                ContextRead.Fields(own[^1]).Zip(Enumerable.Range(0, frame.Count).Select(h => frame[h])).ShouldAllBe(p => Tolerance.Identical(p.First, p.Second));
            }
            else
            {
                Enumerable.Range(0, frame.Count).Select(h => frame[h]).ShouldAllBe(v => double.IsNaN(v));
            }
        }
    }

    [Fact]
    public void A_day_appended_to_the_context_while_the_frame_runs_is_seen_from_the_next_date_and_not_on_its_own_date()
    {
        var days = FrameContextFixtures.Consecutive(13, 40, Monday);
        var context = FrameContextFixtures.Build(days.Take(30), FrameContextFixtures.Small);
        var frame = FrameContextFixtures.Frame(["vix", "breadth"], context);
        var next = days[30];

        frame.Step(Minutes.Close(next.Date, Minutes.Open, 100));
        var seenOnTheDate = frame[0];
        context.Append(next).IsSuccess.ShouldBeTrue();
        frame.Step(Minutes.Close(next.Date, Minutes.At(15, 29), 100));

        seenOnTheDate.ShouldBe(days[29].VixClose!.Value, "the day before");
        frame[0].ShouldBe(seenOnTheDate, "the record of the date, appended while the date runs, is not read on it");

        frame.Step(Minutes.Close(days[31].Date, Minutes.Open, 100));

        frame[0].ShouldBe(next.VixClose!.Value, "the next date reads it");
        frame[1].ShouldBe(next.Breadth!.Value);
        frame.At(0, 1).ShouldBe(seenOnTheDate);
    }

    // ---- the ordering guard and the other lanes -------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_bar_the_ordering_guard_skips_does_not_refresh_the_date_lane_or_write_the_history()
    {
        var days = FrameContextFixtures.Consecutive(14, 40, Monday);
        var frame = FeatureFrame.Create(
            [new FeatureSpec("vix"), new FeatureSpec("day_of_week"), new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3)],
            new FrameOptions(Context: FrameContextFixtures.Build(days, FrameContextFixtures.Small))).Value;
        var d = days[10].Date;
        var e = days[11].Date;
        var f = days[12].Date;

        // the frame at the date of record i sees record i - 1
        frame.Step(Minutes.Close(d, Minutes.Open, 100));
        frame.Step(Minutes.Close(e, Minutes.Open, 100));
        var onE = (frame[0], frame[1]);
        var historyBefore = (frame.At(0, 1), frame.At(1, 1));

        // an earlier date arrives late, a repeat of the last minute and an earlier date again: all skipped
        frame.Step(Minutes.Close(d, Minutes.At(15, 29), 100));
        frame.Step(Minutes.Close(e, Minutes.Open, 100));
        frame.Step(Minutes.Close(days[9].Date, Minutes.At(11, 0), 100));

        frame.IgnoredBars.ShouldBe(3);
        frame.Steps.ShouldBe(2);
        onE.Item1.ShouldBe(days[10].VixClose!.Value);
        (frame[0], frame[1]).ShouldBe(onE);
        (frame.At(0, 1), frame.At(1, 1)).ShouldBe(historyBefore);
        frame.At(0, 1).ShouldBe(days[9].VixClose!.Value, "the history still holds the value of the first date: no entry was added by a skipped bar");

        // a later minute of the same date is accepted and writes nothing either; the next date writes one entry
        frame.Step(Minutes.Close(e, Minutes.At(12, 0), 100));
        frame.Steps.ShouldBe(3);
        (frame[0], frame[1]).ShouldBe(onE);
        frame.At(0, 1).ShouldBe(days[9].VixClose!.Value);
        frame.Step(Minutes.Close(f, Minutes.Open, 100));
        frame.At(0, 1).ShouldBe(onE.Item1);
        frame[0].ShouldBe(days[11].VixClose!.Value);
    }

    [Fact]
    public void The_date_lane_does_not_change_the_bar_lanes_of_the_same_frame_in_any_value_history_or_flag()
    {
        var bars = SeriesGenerator.Random(15, 8_000, 12_000, specialSessions: true);
        var barSpecs = new[]
        {
            new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3),
            new FeatureSpec("rsi", Timeframe.M5, PriceSource.Close, 3),
            new FeatureSpec("ema", Timeframe.M15, PriceSource.Close, 4),
            new FeatureSpec("close", Timeframe.D1),
            new FeatureSpec("vwap"),
            new FeatureSpec("gap_pct"),
        };
        var context = FrameContextFixtures.Build(FrameContextFixtures.Covering(bars, 15));
        var plain = FeatureFrame.Create(barSpecs).Value;
        var mixed = FeatureFrame.Create([.. barSpecs, .. FrameContextFixtures.Specs(TestSpecs.ContextNames)], new FrameOptions(Context: context, ExpiryDays: new HashSet<DateOnly>())).Value;

        foreach (var bar in bars)
        {
            plain.Step(bar);
            mixed.Step(bar);

            foreach (var timeframe in new[] { Timeframe.M1, Timeframe.M5, Timeframe.M15, Timeframe.D1 })
            {
                mixed.ClosedBarsAt(timeframe).ShouldBe(plain.ClosedBarsAt(timeframe), timeframe.ToCode());
            }

            for (var h = 0; h < barSpecs.Length; h++)
            {
                for (var lag = 0; lag < plain.HistoryDepth; lag++)
                {
                    Tolerance.Identical(mixed.At(h, lag), plain.At(h, lag)).ShouldBeTrue($"handle {h} lag {lag}");
                }
            }
        }

        mixed.Steps.ShouldBe(plain.Steps);
        mixed.IgnoredBars.ShouldBe(plain.IgnoredBars);
        mixed.IncompleteBarsConsumed.ShouldBe(plain.IncompleteBarsConsumed);
        mixed.BuilderCount.ShouldBe(plain.BuilderCount);
        mixed.IsReady(barSpecs.Length + 11).ShouldBeTrue("the regime");
    }

    [Fact]
    public void The_regime_is_the_formula_of_the_trend_and_the_volatility_class_on_every_date_and_all_nine_occur()
    {
        var seen = new HashSet<int>();
        foreach (var seed in Enumerable.Range(0, 12))
        {
            var days = FrameContextFixtures.Consecutive(seed, 120);
            var frame = FrameContextFixtures.Frame(["regime_trend", "regime_vol", "regime"], FrameContextFixtures.Build(days, FrameContextFixtures.Small));

            foreach (var day in days)
            {
                frame.Step(Minutes.Close(day.Date, Minutes.Open, 100));

                frame.IsReady(2).ShouldBe(frame.IsReady(0) && frame.IsReady(1));
                if (frame.IsReady(2))
                {
                    frame[2].ShouldBe((1 - frame[0]) * 3 + frame[1] + 1);
                    Regime.Name((int)frame[2]).ShouldNotBeNullOrEmpty();
                    seen.Add((int)frame[2]);
                }
            }
        }

        seen.Order().ShouldBe(Enumerable.Range(1, 9));
    }
}
