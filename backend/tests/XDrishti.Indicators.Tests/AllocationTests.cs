using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// After a warm-up the steady-state <c>Update</c> of every indicator and <c>FeatureFrame.Step</c> allocate nothing (criterion 13): the bytes allocated by this thread over 100,000 steps are exactly 0. The bars are
/// built before the measurement and the warm-up (20,000 steps) lets the JIT compile the paths first; the tests are synchronous so the thread counter covers all the work. They hold in Debug and Release.
/// Every measurement goes through <see cref="AllocationProbe"/>: a garbage collection that another thread starts inside the window moves the thread counter by up to one allocation quantum (a few KB) without any
/// allocation of this code, so such an attempt is run again from scratch; zero bytes, or bytes with no collection, are final at once, and nothing is tolerated.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class AllocationTests
{
    private const int WarmUpSteps = 20_000;

    private const int MeasuredSteps = 100_000;

    /// <summary>Gap-free, time-ordered bars for warm-up and measurement: 330 sessions of 375 minutes.</summary>
    private static readonly Bar[] Bars = [.. SeriesGenerator.Sessions(99, 330)];

    /// <summary>
    /// The awkward series: missing minutes (incomplete higher-timeframe buckets), zero volume, flat bars, gap-ups, sessions that end early, Saturday and Muhurat sessions. Ordered and long enough for warm-up plus measurement.
    /// </summary>
    private static readonly Bar[] GappyBars = [.. SeriesGenerator.Random(123, WarmUpSteps + MeasuredSteps + 5_000, WarmUpSteps + MeasuredSteps + 5_000, specialSessions: true)];

    private static readonly TradingCalendar GappyCalendar = CalendarFixtures.For(GappyBars);

    /// <summary>Consecutive weekdays from mid-2024 for 1,800 days (to 2031): the dates of both series are inside, with a warm-up of more than 265 days before them, so every context feature has a number.</summary>
    private static readonly MarketContext Context = FrameContextFixtures.Build(FrameContextFixtures.Consecutive(77, 1_800));

    /// <summary>Every Tuesday of 2026 and 2027: the expiry dates of the frames below (a lookup that hits and one that misses both happen).</summary>
    private static readonly IReadOnlySet<DateOnly> Tuesdays = new HashSet<DateOnly>(FrameContextFixtures.Dates(new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31)).Where(d => d.DayOfWeek == DayOfWeek.Tuesday));

    [Fact]
    public void The_series_are_long_enough_and_ordered()
    {
        foreach (var bars in new[] { Bars, GappyBars })
        {
            bars.Length.ShouldBeGreaterThanOrEqualTo(WarmUpSteps + MeasuredSteps);
            for (var i = 1; i < bars.Length; i++)
            {
                bars[i].EpochSeconds.ShouldBeGreaterThan(bars[i - 1].EpochSeconds);
            }
        }
    }

    [Fact]
    public void The_gappy_series_really_has_gaps_zero_volume_and_special_sessions()
    {
        var days = GappyBars.GroupBy(b => SessionClock.DateOf(b.EpochSeconds)).ToList();

        GappyBars.Count(b => b.Volume == 0).ShouldBeGreaterThan(1_000);
        days.Count(d => d.Count() < 375).ShouldBeGreaterThan(10);
        days.Count(d => d.Key.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday).ShouldBeGreaterThan(5);
        var probe = TwentyFeatureFrame();
        foreach (var bar in GappyBars.Take(WarmUpSteps))
        {
            probe.Step(bar);
        }

        probe.IncompleteBarsConsumed.ShouldBeGreaterThan(0, "incomplete higher-timeframe buckets are consumed");
    }

    [Fact]
    public void The_measurement_notices_an_allocation_when_there_is_one()
    {
        object? keep = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            keep = new byte[16];
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeGreaterThanOrEqualTo(16_000);
        GC.KeepAlive(keep);
    }

    [Fact]
    public void Update_allocates_nothing_for_every_indicator()
    {
        foreach (var spec in IndicatorCatalog.SupportedTimeframes.SelectMany(TestSpecs.Variants).Where(s => s.Timeframe == Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();

            AllocationProbe.ShouldAllocateNothing(window =>
            {
                var indicator = info.Create(spec);
                for (var i = 0; i < WarmUpSteps; i++)
                {
                    indicator.Update(Bars[i]);
                }

                window.Begin();
                for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
                {
                    indicator.Update(Bars[i]);
                }

                window.End();
            }, TestSpecs.Describe(spec));
        }
    }

    [Fact]
    public void Update_allocates_nothing_for_every_indicator_on_the_gappy_series()
    {
        foreach (var spec in TestSpecs.Variants(Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();

            AllocationProbe.ShouldAllocateNothing(window =>
            {
                var indicator = info.Create(spec);
                for (var i = 0; i < WarmUpSteps; i++)
                {
                    indicator.Update(GappyBars[i]);
                }

                window.Begin();
                for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
                {
                    indicator.Update(GappyBars[i]);
                }

                window.End();
            }, TestSpecs.Describe(spec));
        }
    }

    [Fact]
    public void Update_and_reading_the_value_allocate_nothing_through_the_interface()
    {
        AllocationProbe.ShouldAllocateNothing(window =>
        {
            IIndicator[] indicators =
            [
                new Sma(20), new Ema(20), new Rsi(14), new Atr(14), new Highest(20), new Lowest(20), new Vwap(), new RelVolume(20), new OrHigh(15), new OrLow(15), new SessionContext(SessionField.GapPct),
                new Adx(14), new PlusDi(14), new MinusDi(14), new MacdLine(12, 26, 9), new MacdSignal(12, 26, 9), new MacdHist(12, 26, 9), new BbUpper(20, 2), new BbMiddle(20, 2), new BbLower(20, 2), new BbWidth(20, 2),
                new Supertrend(10, 3), new SupertrendDirection(10, 3), new Roc(10), new InsideBar(), new NarrowRange(7), new Engulfing(), new PivotHigh(5), new PivotLow(5), new PivotHighAge(5), new PivotLowAge(5), new RawBar(PriceSource.Close),
            ];
            foreach (var indicator in indicators)
            {
                for (var i = 0; i < WarmUpSteps; i++)
                {
                    indicator.Update(Bars[i]);
                }
            }

            var sink = 0.0;
            window.Begin();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                foreach (var indicator in indicators)
                {
                    indicator.Update(Bars[i]);
                    sink += indicator.IsReady ? indicator.Value : 0;
                }
            }

            window.End();
            double.IsNaN(sink).ShouldBeFalse();
        });
    }

    [Fact]
    public void The_timeframe_builders_allocate_nothing()
    {
        foreach (var timeframe in TestSpecs.DerivedTimeframes)
        {
            AllocationProbe.ShouldAllocateNothing(window =>
            {
                var builder = new TimeframeBuilder(timeframe);
                for (var i = 0; i < WarmUpSteps; i++)
                {
                    builder.Push(Bars[i], out _, out _);
                }

                window.Begin();
                for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
                {
                    builder.Push(Bars[i], out _, out _);
                }

                window.End();
            }, timeframe.ToCode());
        }
    }

    [Fact]
    public void The_timeframe_builders_allocate_nothing_on_the_gappy_series_with_and_without_a_calendar()
    {
        foreach (var calendar in new TradingCalendar?[] { null, GappyCalendar })
        {
            foreach (var timeframe in TestSpecs.DerivedTimeframes)
            {
                AllocationProbe.ShouldAllocateNothing(window =>
                {
                    var builder = new TimeframeBuilder(timeframe, calendar);
                    for (var i = 0; i < WarmUpSteps; i++)
                    {
                        builder.Push(GappyBars[i], out _, out _);
                    }

                    window.Begin();
                    for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
                    {
                        builder.Push(GappyBars[i], out _, out _);
                    }

                    window.End();
                }, $"{timeframe.ToCode()}, calendar {calendar is not null}");
            }
        }
    }

    [Fact]
    public void A_builder_that_consults_a_calendar_allocates_nothing()
    {
        var calendar = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 3, 4), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);

        AllocationProbe.ShouldAllocateNothing(window =>
        {
            var builder = new TimeframeBuilder(Timeframe.M15, calendar);
            for (var i = 0; i < WarmUpSteps; i++)
            {
                builder.Push(Bars[i], out _, out _);
            }

            window.Begin();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                builder.Push(Bars[i], out _, out _);
            }

            window.End();
        });
    }

    [Fact]
    public void A_twelve_feature_frame_step_allocates_nothing()
    {
        AllocationProbe.ShouldAllocateNothing(window =>
        {
            var frame = TwelveFeatureFrame();
            for (var i = 0; i < WarmUpSteps; i++)
            {
                frame.Step(Bars[i]);
            }

            var sink = 0.0;
            window.Begin();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                frame.Step(Bars[i]);
                for (var h = 0; h < frame.Count; h++)
                {
                    sink += frame.IsReady(h) ? frame[h] : 0;
                }
            }

            window.End();
            double.IsNaN(sink).ShouldBeFalse();
        });
    }

    [Fact]
    public void A_twenty_feature_frame_with_history_allocates_nothing_on_the_gap_free_series()
    {
        AssertFrameAllocatesNothing(() => TwentyFeatureFrame(), Bars);
        AssertFrameAllocatesNothing(() => ThirtyFiveFeatureFrame(), Bars);
    }

    [Fact]
    public void A_twenty_feature_frame_with_history_allocates_nothing_on_the_gappy_series_with_and_without_a_calendar()
    {
        AssertFrameAllocatesNothing(() => TwentyFeatureFrame(), GappyBars);
        AssertFrameAllocatesNothing(() => ThirtyFiveFeatureFrame(), GappyBars);
        AssertFrameAllocatesNothing(() => TwentyFeatureFrame(GappyCalendar), GappyBars);
        AssertFrameAllocatesNothing(() => ThirtyFiveFeatureFrame(GappyCalendar), GappyBars);
    }

    [Fact]
    public void A_frame_with_the_one_minute_hour_day_week_and_month_lanes_allocates_nothing_while_weeks_and_months_close()
    {
        var closes = AssertFrameAllocatesNothing(() => PeriodLaneFrame(), Bars);

        closes[(int)Timeframe.W1].ShouldBeGreaterThan(40, "about 53 weeks close in the measured window");
        closes[(int)Timeframe.Mo1].ShouldBeGreaterThan(8, "about 12 months close in the measured window");
        closes[(int)Timeframe.D1].ShouldBeGreaterThan(250);
    }

    [Fact]
    public void A_frame_with_the_weekly_and_monthly_lanes_allocates_nothing_on_the_gappy_series_with_and_without_a_calendar()
    {
        foreach (var calendar in new TradingCalendar?[] { null, GappyCalendar })
        {
            var closes = AssertFrameAllocatesNothing(() => PeriodLaneFrame(calendar), GappyBars);

            closes[(int)Timeframe.W1].ShouldBeGreaterThan(40, $"calendar {calendar is not null}");
            closes[(int)Timeframe.Mo1].ShouldBeGreaterThan(8, $"calendar {calendar is not null}");
        }
    }

    [Fact]
    public void The_weekly_and_monthly_lanes_stay_allocation_free_when_the_ordering_guard_skips_bars()
    {
        AllocationProbe.ShouldAllocateNothing(window =>
        {
            var frame = PeriodLaneFrame(GappyCalendar);
            for (var i = 0; i < WarmUpSteps; i++)
            {
                frame.Step(GappyBars[i]);
            }

            window.Begin();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                frame.Step(GappyBars[i]);
                frame.Step(GappyBars[i - 1]);
            }

            window.End();
            frame.IgnoredBars.ShouldBe(MeasuredSteps);
        });
    }

    [Fact]
    public void A_reset_frame_with_weekly_and_monthly_lanes_allocates_nothing_in_steady_state_again()
    {
        AllocationProbe.ShouldAllocateNothing(window =>
        {
            var frame = PeriodLaneFrame();
            for (var i = 0; i < WarmUpSteps; i++)
            {
                frame.Step(Bars[i]);
            }

            frame.Reset();
            for (var i = 0; i < WarmUpSteps; i++)
            {
                frame.Step(Bars[i]);
            }

            window.Begin();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                frame.Step(Bars[i]);
            }

            window.End();
        });
    }

    [Fact]
    public void The_frame_stays_allocation_free_when_the_ordering_guard_skips_bars()
    {
        AllocationProbe.ShouldAllocateNothing(window =>
        {
            var frame = TwentyFeatureFrame();
            for (var i = 0; i < WarmUpSteps; i++)
            {
                frame.Step(Bars[i]);
            }

            window.Begin();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                frame.Step(Bars[i]);
                frame.Step(Bars[i]);
            }

            window.End();
            frame.IgnoredBars.ShouldBe(MeasuredSteps);
        });
    }

    [Fact]
    public void The_date_driven_indicators_allocate_nothing_through_the_interface_including_the_step_that_changes_the_date()
    {
        AllocationProbe.ShouldAllocateNothing(window =>
        {
            IIndicator[] indicators = [.. Enum.GetValues<ContextFeatureKind>().Select(kind => new ContextFeature(kind, Context, Tuesdays))];
            foreach (var indicator in indicators)
            {
                for (var i = 0; i < WarmUpSteps; i++)
                {
                    indicator.Update(GappyBars[i]);
                }
            }

            var sink = 0.0;
            window.Begin();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                foreach (var indicator in indicators)
                {
                    indicator.Update(GappyBars[i]);
                    sink += indicator.IsReady ? indicator.Value : 0;
                }
            }

            window.End();
            double.IsNaN(sink).ShouldBeFalse();
        });

        DatesIn(GappyBars).ShouldBeGreaterThan(100, "the measured window changes the date more than a hundred times");
    }

    [Fact]
    public void A_twenty_feature_frame_with_six_context_features_allocates_nothing_including_the_date_change_step()
    {
        foreach (var bars in new[] { Bars, GappyBars })
        {
            foreach (var calendar in new TradingCalendar?[] { null, bars == GappyBars ? GappyCalendar : new TradingCalendar() })
            {
                FeatureFrame? measured = null;
                AssertFrameAllocatesNothing(() => measured = TwentyFeatureFrameWithContext(calendar), bars);

                measured.ShouldNotBeNull().Count.ShouldBe(26);
                DatesIn(bars).ShouldBeGreaterThan(100, "the measured window changes the date more than a hundred times");
                measured.IsReady(measured.HandleOf(new FeatureSpec("regime"))).ShouldBeTrue("the context features have numbers in the window");
                measured.IsReady(measured.HandleOf(new FeatureSpec("vix_pct"))).ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void A_frame_of_all_twelve_date_driven_names_allocates_nothing_also_with_skipped_bars_and_after_a_reset()
    {
        AllocationProbe.ShouldAllocateNothing(window =>
        {
            var frame = FrameContextFixtures.Frame(TestSpecs.ContextNames, Context, Tuesdays);
            for (var i = 0; i < WarmUpSteps; i++)
            {
                frame.Step(GappyBars[i]);
            }

            frame.Reset();
            for (var i = 0; i < WarmUpSteps; i++)
            {
                frame.Step(GappyBars[i]);
            }

            var sink = 0.0;
            window.Begin();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                frame.Step(GappyBars[i]);
                frame.Step(GappyBars[i - 1]);
                for (var h = 0; h < frame.Count; h++)
                {
                    sink += frame.IsReady(h) ? frame[h] : 0;
                    sink += frame.IsReadyAt(h, 1) ? frame.At(h, 1) : 0;
                }
            }

            window.End();
            frame.IgnoredBars.ShouldBe(MeasuredSteps);
            double.IsNaN(sink).ShouldBeFalse();
        });
    }

    /// <summary>How many times the India date changes between two consecutive bars of the measured window.</summary>
    private static int DatesIn(Bar[] bars)
    {
        var changes = 0;
        for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
        {
            changes += FrameContextFixtures.DateOf(bars[i]) != FrameContextFixtures.DateOf(bars[i - 1]) ? 1 : 0;
        }

        return changes;
    }

    /// <summary>
    /// Steps a fresh frame through warm-up and measurement, reading values, history, readiness and the closed flags the way the DSL does; nothing may be allocated. The frame comes from a factory because an attempt
    /// that a garbage collection disturbed (see <see cref="AllocationProbe"/>) is run again with a new one. Returns how many bars each timeframe closed in the measured window (indexed by <c>(int)Timeframe</c>),
    /// so that a test can say that weeks and months really closed while nothing was allocated.
    /// </summary>
    private static int[] AssertFrameAllocatesNothing(Func<FeatureFrame> createFrame, Bar[] bars)
    {
        var closesPerTimeframe = new int[Enum.GetValues<Timeframe>().Length];
        AllocationProbe.ShouldAllocateNothing(window =>
        {
            var frame = createFrame();
            frame.Count.ShouldBeGreaterThanOrEqualTo(20);
            frame.HistoryDepth.ShouldBe(8);
            var timeframes = IndicatorCatalog.SupportedTimeframes.Where(frame.HasTimeframe).ToArray();
            for (var i = 0; i < WarmUpSteps; i++)
            {
                frame.Step(bars[i]);
            }

            var sink = 0.0;
            var closed = 0;
            Array.Clear(closesPerTimeframe);
            window.Begin();
            for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
            {
                frame.Step(bars[i]);
                for (var h = 0; h < frame.Count; h++)
                {
                    sink += frame.IsReady(h) ? frame[h] : 0;
                    sink += frame.IsReadyAt(h, 1) ? frame.At(h, 1) : 0;
                    var deep = frame.At(h, 7);
                    sink += double.IsNaN(deep) ? 0 : deep;
                }

                for (var t = 0; t < timeframes.Length; t++)
                {
                    if (frame.BarClosed(timeframes[t]) && frame.TryLastClosed(timeframes[t], out var last))
                    {
                        closed += last.Bars;
                        closesPerTimeframe[(int)timeframes[t]] += frame.ClosedBarsAt(timeframes[t]);
                    }
                }
            }

            window.End();
            double.IsNaN(sink).ShouldBeFalse();
            closed.ShouldBeGreaterThan(0);
        });

        return closesPerTimeframe;
    }

    /// <summary>Twenty features over five timeframes with the default history depth of 8: the mix a strategy would use, new indicators included (also the benchmark frame).</summary>
    internal static FeatureFrame TwentyFeatureFrame(TradingCalendar? calendar = null) => FeatureFrame.Create(TwentySpecs(), new FrameOptions(calendar)).Value;

    /// <summary>
    /// The twenty features of <see cref="TwentyFeatureFrame"/> and six that read the calendar and the market context (<c>vix</c>, <c>vix_pct</c>, <c>index_trend</c>, <c>regime</c>, <c>day_of_week</c>,
    /// <c>is_expiry_day</c>): 26 handles. Also the benchmark frame of the context features. The context and the expiry dates default to the ones of this class.
    /// </summary>
    internal static FeatureFrame TwentyFeatureFrameWithContext(TradingCalendar? calendar = null, MarketContext? context = null, IReadOnlySet<DateOnly>? expiryDays = null) => FeatureFrame.Create(
    [
        .. TwentySpecs(),
        new FeatureSpec("vix"),
        new FeatureSpec("vix_pct"),
        new FeatureSpec("index_trend"),
        new FeatureSpec("regime"),
        new FeatureSpec("day_of_week"),
        new FeatureSpec("is_expiry_day"),
    ],
    new FrameOptions(calendar, FrameOptions.DefaultHistoryDepth, context ?? Context, expiryDays ?? Tuesdays)).Value;

    private static FeatureSpec[] TwentySpecs() =>
    [
        new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 20),
        new FeatureSpec("rsi", Timeframe.M1, PriceSource.Close, 14),
        new FeatureSpec("vwap", Timeframe.M1),
        new FeatureSpec("rel_volume", Timeframe.M1, PriceSource.Close, 20),
        new FeatureSpec("adx", Timeframe.M1, PriceSource.Close, 14),
        new FeatureSpec("macd_hist", Timeframe.M1, PriceSource.Close, 12, 26, 9),
        new FeatureSpec("roc", Timeframe.M1, PriceSource.Close, 10),
        new FeatureSpec("close", Timeframe.M1),
        new FeatureSpec("ema", Timeframe.M5, PriceSource.Close, 20),
        new FeatureSpec("supertrend", Timeframe.M5, PriceSource.Close, 10, 3),
        new FeatureSpec("bb_width", Timeframe.M5, PriceSource.Close, 20, 2),
        new FeatureSpec("pivot_high", Timeframe.M5, PriceSource.Close, 2),
        new FeatureSpec("nr", Timeframe.M5, PriceSource.Close, 7),
        new FeatureSpec("engulfing", Timeframe.M5),
        new FeatureSpec("rsi", Timeframe.M15, PriceSource.Close, 14),
        new FeatureSpec("highest", Timeframe.M15, PriceSource.High, 20),
        new FeatureSpec("plus_di", Timeframe.M15, PriceSource.Close, 14),
        new FeatureSpec("atr", Timeframe.H1, PriceSource.Close, 14),
        new FeatureSpec("macd_line", Timeframe.H1, PriceSource.Close, 12, 26, 9),
        new FeatureSpec("inside_bar", Timeframe.D1),
    ];

    /// <summary>Every new feature at least once, on all timeframes (35 features), history depth 8.</summary>
    internal static FeatureFrame ThirtyFiveFeatureFrame(TradingCalendar? calendar = null) => FeatureFrame.Create(
    [
        new FeatureSpec("adx", Timeframe.M5, PriceSource.Close, 14),
        new FeatureSpec("plus_di", Timeframe.M5, PriceSource.Close, 14),
        new FeatureSpec("minus_di", Timeframe.M15, PriceSource.Close, 14),
        new FeatureSpec("macd_line", Timeframe.M1, PriceSource.Close, 12, 26, 9),
        new FeatureSpec("macd_signal", Timeframe.M5, PriceSource.Close, 12, 26, 9),
        new FeatureSpec("macd_hist", Timeframe.M15, PriceSource.Close, 12, 26, 9),
        new FeatureSpec("bb_upper", Timeframe.M1, PriceSource.Close, 20, 2),
        new FeatureSpec("bb_middle", Timeframe.M5, PriceSource.Close, 20, 2),
        new FeatureSpec("bb_lower", Timeframe.M15, PriceSource.Close, 20, 2),
        new FeatureSpec("bb_width", Timeframe.H1, PriceSource.Close, 20, 2),
        new FeatureSpec("supertrend", Timeframe.M1, PriceSource.Close, 10, 3),
        new FeatureSpec("supertrend_dir", Timeframe.M15, PriceSource.Close, 10, 3),
        new FeatureSpec("roc", Timeframe.M30, PriceSource.Close, 10),
        new FeatureSpec("inside_bar", Timeframe.M1),
        new FeatureSpec("nr", Timeframe.M10, PriceSource.Close, 7),
        new FeatureSpec("engulfing", Timeframe.M30),
        new FeatureSpec("pivot_high", Timeframe.M1, PriceSource.Close, 5),
        new FeatureSpec("pivot_low", Timeframe.M5, PriceSource.Close, 3),
        new FeatureSpec("pivot_high_age", Timeframe.M15, PriceSource.Close, 2),
        new FeatureSpec("pivot_low_age", Timeframe.D1, PriceSource.Close, 1),
        new FeatureSpec("open", Timeframe.M1),
        new FeatureSpec("high", Timeframe.M5),
        new FeatureSpec("low", Timeframe.M15),
        new FeatureSpec("close", Timeframe.H1),
        new FeatureSpec("volume", Timeframe.D1),
        new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 20),
        new FeatureSpec("ema", Timeframe.M10, PriceSource.Close, 20),
        new FeatureSpec("rsi", Timeframe.M30, PriceSource.Close, 14),
        new FeatureSpec("atr", Timeframe.H1, PriceSource.Close, 14),
        new FeatureSpec("highest", Timeframe.D1, PriceSource.High, 5),
        new FeatureSpec("vwap", Timeframe.M5),
        new FeatureSpec("rel_volume", Timeframe.M15, PriceSource.Close, 20),
        new FeatureSpec("or_high", Timeframe.M5, PriceSource.Close, 15),
        new FeatureSpec("gap_pct", Timeframe.D1),
        new FeatureSpec("prev_day_close", Timeframe.M15),
    ],
    new FrameOptions(calendar)).Value;

    /// <summary>
    /// 28 features on the 1m, 1h, 1D, 1W and 1M lanes together, history depth 8: the weekly and monthly lanes carry the trend, momentum, volatility, band, pattern and raw bar features a strategy would read on them
    /// (also the benchmark frame of the weekly and monthly lanes).
    /// </summary>
    internal static FeatureFrame PeriodLaneFrame(TradingCalendar? calendar = null) => FeatureFrame.Create(
    [
        new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 20),
        new FeatureSpec("rsi", Timeframe.M1, PriceSource.Close, 14),
        new FeatureSpec("vwap", Timeframe.M1),
        new FeatureSpec("adx", Timeframe.M1, PriceSource.Close, 14),
        new FeatureSpec("ema", Timeframe.H1, PriceSource.Close, 20),
        new FeatureSpec("atr", Timeframe.H1, PriceSource.Close, 14),
        new FeatureSpec("sma", Timeframe.D1, PriceSource.Close, 5),
        new FeatureSpec("close", Timeframe.D1),
        new FeatureSpec("rsi", Timeframe.D1, PriceSource.Close, 14),
        new FeatureSpec("prev_day_close", Timeframe.D1),
        new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 10),
        new FeatureSpec("ema", Timeframe.W1, PriceSource.Close, 10),
        new FeatureSpec("rsi", Timeframe.W1, PriceSource.Close, 14),
        new FeatureSpec("atr", Timeframe.W1, PriceSource.Close, 14),
        new FeatureSpec("adx", Timeframe.W1, PriceSource.Close, 14),
        new FeatureSpec("macd_hist", Timeframe.W1, PriceSource.Close, 12, 26, 9),
        new FeatureSpec("bb_width", Timeframe.W1, PriceSource.Close, 20, 2),
        new FeatureSpec("supertrend", Timeframe.W1, PriceSource.Close, 10, 3),
        new FeatureSpec("close", Timeframe.W1),
        new FeatureSpec("volume", Timeframe.W1),
        new FeatureSpec("highest", Timeframe.W1, PriceSource.High, 20),
        new FeatureSpec("inside_bar", Timeframe.W1),
        new FeatureSpec("pivot_high", Timeframe.W1, PriceSource.Close, 2),
        new FeatureSpec("sma", Timeframe.Mo1, PriceSource.Close, 6),
        new FeatureSpec("rsi", Timeframe.Mo1, PriceSource.Close, 6),
        new FeatureSpec("roc", Timeframe.Mo1, PriceSource.Close, 3),
        new FeatureSpec("close", Timeframe.Mo1),
        new FeatureSpec("open", Timeframe.Mo1),
    ],
    new FrameOptions(calendar)).Value;

    /// <summary>Twelve features over four timeframes, the mix a strategy would use.</summary>
    internal static FeatureFrame TwelveFeatureFrame() => FeatureFrame.Create(
    [
        new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 20),
        new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, 50),
        new FeatureSpec("rsi", Timeframe.M1, PriceSource.Close, 14),
        new FeatureSpec("atr", Timeframe.M1, PriceSource.Close, 14),
        new FeatureSpec("vwap", Timeframe.M1),
        new FeatureSpec("rel_volume", Timeframe.M1, PriceSource.Close, 20),
        new FeatureSpec("ema", Timeframe.M5, PriceSource.Close, 20),
        new FeatureSpec("rsi", Timeframe.M15, PriceSource.Close, 14),
        new FeatureSpec("highest", Timeframe.M15, PriceSource.High, 20),
        new FeatureSpec("atr", Timeframe.H1, PriceSource.Close, 14),
        new FeatureSpec("or_high", Timeframe.M1, PriceSource.Close, 15),
        new FeatureSpec("gap_pct", Timeframe.M1),
    ]).Value;
}
