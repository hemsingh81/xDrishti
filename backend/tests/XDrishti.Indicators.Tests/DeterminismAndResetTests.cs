using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>Two runs over the same input give bit-identical outputs (criterion 10); <c>Reset()</c> returns every indicator to its state after construction (criterion 2).</summary>
public sealed class DeterminismAndResetTests
{
    private static List<long> Bits(List<Bar> bars, List<FeatureSpec> specs, FrameOptions? options = null)
    {
        var frame = FeatureFrame.Create(specs, options).Value;
        var bits = new List<long>(bars.Count * specs.Count);
        foreach (var bar in bars)
        {
            frame.Step(bar);
            for (var h = 0; h < specs.Count; h++)
            {
                bits.Add(BitConverter.DoubleToInt64Bits(frame[h]));
                bits.Add(BitConverter.DoubleToInt64Bits(frame.At(h, 1)));
                bits.Add(BitConverter.DoubleToInt64Bits(frame.At(h, 7)));
            }

            foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
            {
                if (frame.HasTimeframe(timeframe))
                {
                    bits.Add(frame.ClosedBarsAt(timeframe));
                }
            }
        }

        return bits;
    }

    [Fact]
    public void Two_runs_over_the_same_bars_give_the_same_bits_also_on_another_thread()
    {
        var bars = SeriesGenerator.Random(5, specialSessions: true);
        var specs = IndicatorCatalog.SupportedTimeframes.SelectMany(TestSpecs.Variants).ToList();

        var first = Bits(bars, specs);
        var second = Bits(bars, specs);
        List<long>? third = null;
        var thread = new Thread(() => third = Bits(bars, specs));
        thread.Start();
        thread.Join();

        second.ShouldBe(first);
        third.ShouldNotBeNull().ShouldBe(first);
    }

    /// <summary>A frame with the 1m, 1h, 1D, 1W and 1M lanes together and every kind of weekly and monthly feature.</summary>
    private static List<FeatureSpec> PeriodSpecs() =>
    [
        new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 5),
        new FeatureSpec("rsi", Timeframe.H1, PriceSource.Close, 3),
        new FeatureSpec("close", Timeframe.D1),
        .. TestSpecs.PeriodTimeframes.SelectMany(TestSpecs.Representatives),
        new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 3),
        new FeatureSpec("ema", Timeframe.W1, PriceSource.Close, 4),
        new FeatureSpec("macd_hist", Timeframe.Mo1, PriceSource.Close, 2, 3, 2),
    ];

    [Fact]
    public void Two_runs_over_the_same_weeks_and_months_give_the_same_bits_also_on_another_thread()
    {
        var series = SeriesGenerator.Thin(5, 600);
        var options = PeriodCalendars.For(series, CalendarKnowledge.Full).Options();
        var specs = PeriodSpecs();

        var first = Bits(series.Bars, specs, options);
        var second = Bits(series.Bars, specs, options);
        List<long>? third = null;
        var thread = new Thread(() => third = Bits(series.Bars, specs, options));
        thread.Start();
        thread.Join();

        second.ShouldBe(first);
        third.ShouldNotBeNull().ShouldBe(first);
        first.Distinct().Count().ShouldBeGreaterThan(1_000, "the run produced many different values, not a row of NaN");
    }

    [Fact]
    public void Two_instances_fed_the_same_bars_agree_to_the_last_bit()
    {
        var bars = SeriesGenerator.Random(11);

        foreach (var spec in TestSpecs.Variants(Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var a = info.Create(spec);
            var b = info.Create(spec);
            foreach (var bar in bars)
            {
                a.Update(bar);
                b.Update(bar);
                Tolerance.Identical(a.Value, b.Value).ShouldBeTrue(TestSpecs.Describe(spec));
            }
        }
    }

    [Fact]
    public void Reset_returns_every_indicator_to_the_state_after_construction()
    {
        var before = SeriesGenerator.Random(21, 800, 1_500, specialSessions: true);
        var after = SeriesGenerator.Random(22, 800, 1_500);

        foreach (var spec in TestSpecs.Variants(Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var used = info.Create(spec);
            foreach (var bar in before)
            {
                used.Update(bar);
            }

            used.Reset();
            var fresh = info.Create(spec);

            used.IsReady.ShouldBe(fresh.IsReady, TestSpecs.Describe(spec));
            Tolerance.Identical(used.Value, fresh.Value).ShouldBeTrue(TestSpecs.Describe(spec));
            foreach (var bar in after)
            {
                used.Update(bar);
                fresh.Update(bar);
                if (!Tolerance.Identical(used.Value, fresh.Value) || used.IsReady != fresh.IsReady)
                {
                    Assert.Fail($"{TestSpecs.Describe(spec)} differs after Reset at {bar.Start:u}");
                }
            }
        }
    }

    [Fact]
    public void A_frame_that_was_reset_equals_a_new_one()
    {
        var specs = IndicatorCatalog.SupportedTimeframes.SelectMany(TestSpecs.Variants).ToList();
        var bars = SeriesGenerator.Random(31, 600, 900, specialSessions: true);
        var used = FeatureFrame.Create(specs).Value;
        foreach (var bar in SeriesGenerator.Random(32, 600, 900))
        {
            used.Step(bar);
        }

        used.Reset();
        var fresh = FeatureFrame.Create(specs).Value;
        foreach (var bar in bars)
        {
            used.Step(bar);
            fresh.Step(bar);
            for (var h = 0; h < specs.Count; h++)
            {
                Tolerance.Identical(used[h], fresh[h]).ShouldBeTrue($"{TestSpecs.Describe(specs[h])} at {bar.Start:u}");
                Tolerance.Identical(used.At(h, 1), fresh.At(h, 1)).ShouldBeTrue($"{TestSpecs.Describe(specs[h])} At(h, 1) at {bar.Start:u}");
                Tolerance.Identical(used.At(h, 7), fresh.At(h, 7)).ShouldBeTrue($"{TestSpecs.Describe(specs[h])} At(h, 7) at {bar.Start:u}");
            }

            foreach (var timeframe in IndicatorCatalog.SupportedTimeframes.Where(used.HasTimeframe))
            {
                used.ClosedBarsAt(timeframe).ShouldBe(fresh.ClosedBarsAt(timeframe), timeframe.ToCode());
            }
        }
    }

    [Theory]
    [InlineData(CalendarKnowledge.None)]
    [InlineData(CalendarKnowledge.Full)]
    public void A_frame_with_weekly_and_monthly_lanes_that_was_reset_equals_a_new_one(CalendarKnowledge kind)
    {
        var earlier = SeriesGenerator.Thin(32, 400);
        var series = SeriesGenerator.Thin(31, 600);
        var options = PeriodCalendars.For(series, kind).Options();
        var specs = PeriodSpecs();
        var used = FeatureFrame.Create(specs, options).Value;

        // a run that stops in the middle of a week and a month, so there is an open period, a last emitted one and a position for the ordering guard to forget
        foreach (var bar in earlier.Bars.Take(earlier.Bars.Count / 2 + 7))
        {
            used.Step(bar);
        }

        used.Reset();
        var fresh = FeatureFrame.Create(specs, options).Value;
        used.Steps.ShouldBe(0);
        used.IgnoredBars.ShouldBe(0);
        used.IncompleteBarsConsumed.ShouldBe(0);
        foreach (var timeframe in new[] { Timeframe.D1, Timeframe.W1, Timeframe.Mo1 })
        {
            used.BarClosed(timeframe).ShouldBeFalse();
            used.TryLastClosed(timeframe, out _).ShouldBeFalse();
        }

        var weeks = 0;
        foreach (var bar in series.Bars)
        {
            used.Step(bar);
            fresh.Step(bar);
            weeks += fresh.ClosedBarsAt(Timeframe.W1);
            for (var h = 0; h < specs.Count; h++)
            {
                if (!Tolerance.Identical(used[h], fresh[h]) || !Tolerance.Identical(used.At(h, 1), fresh.At(h, 1)) || !Tolerance.Identical(used.At(h, 7), fresh.At(h, 7)))
                {
                    Assert.Fail($"{TestSpecs.Describe(specs[h])} differs after Reset at {bar.Start:u}");
                }
            }

            foreach (var timeframe in new[] { Timeframe.M1, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1 })
            {
                used.ClosedBarsAt(timeframe).ShouldBe(fresh.ClosedBarsAt(timeframe), timeframe.ToCode());
            }
        }

        (used.Steps, used.IgnoredBars, used.IncompleteBarsConsumed).ShouldBe((fresh.Steps, fresh.IgnoredBars, fresh.IncompleteBarsConsumed));
        weeks.ShouldBeGreaterThan(40);
    }

    [Theory]
    [InlineData(CalendarKnowledge.None)]
    [InlineData(CalendarKnowledge.Full)]
    public void A_frame_that_replays_the_same_weeks_after_a_reset_gives_the_bits_of_the_first_pass(CalendarKnowledge kind)
    {
        // The ordering guard and the period builders remember the last bar and the last period emitted: after Reset the same early dates must be accepted again.
        var series = SeriesGenerator.Thin(9, 500);
        var options = PeriodCalendars.For(series, kind).Options();
        var specs = PeriodSpecs();
        var frame = FeatureFrame.Create(specs, options).Value;
        var firstPass = new List<long>();
        var secondPass = new List<long>();

        foreach (var (pass, sink) in new[] { (0, firstPass), (1, secondPass) })
        {
            frame.Reset();
            foreach (var bar in series.Bars)
            {
                frame.Step(bar);
                for (var h = 0; h < specs.Count; h++)
                {
                    sink.Add(BitConverter.DoubleToInt64Bits(frame[h]));
                    sink.Add(BitConverter.DoubleToInt64Bits(frame.At(h, 3)));
                }

                sink.Add(frame.ClosedBarsAt(Timeframe.W1) * 10L + frame.ClosedBarsAt(Timeframe.Mo1));
            }

            frame.IgnoredBars.ShouldBe(0, $"pass {pass}: no bar was skipped");
        }

        secondPass.ShouldBe(firstPass);
    }

    [Fact]
    public void Indicators_and_messages_do_not_depend_on_the_current_culture_also_with_a_comma_decimal_one()
    {
        var bars = SeriesGenerator.Random(41, 600, 800);
        var specs = TestSpecs.Variants(Timeframe.M1);
        var expected = Bits(bars, specs);
        var weeks = SeriesGenerator.Thin(41, 200);
        var weekSpecs = PeriodSpecs();
        var expectedWeeks = Bits(weeks.Bars, weekSpecs);
        var invalid = new[] { new FeatureSpec("bb_upper", Timeframe.M1, PriceSource.Close, 20, 0), new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, 1.5), new FeatureSpec("macd_line", Timeframe.M1, PriceSource.Close, 26, 12, 9), new FeatureSpec("vwap", Timeframe.W1), new FeatureSpec("gap_pct", Timeframe.Mo1) };
        var expectedMessages = FeatureFrame.Create(invalid).Error.Fields!.Select(f => f.Message).ToList();

        // a culture with a comma as the decimal separator, a dot for groups and an unusual minus sign: any culture-sensitive formatting or parsing would show
        var comma = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";
        comma.NumberFormat.NegativeSign = "~";
        var original = Thread.CurrentThread.CurrentCulture;
        var originalUi = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = comma;
            Thread.CurrentThread.CurrentUICulture = comma;

            Bits(bars, specs).ShouldBe(expected);
            Bits(weeks.Bars, weekSpecs).ShouldBe(expectedWeeks);
            FeatureFrame.Create(invalid).Error.Fields!.Select(f => f.Message).ShouldBe(expectedMessages);
            TestSpecs.Describe(new FeatureSpec("bb_upper", Timeframe.M1, PriceSource.Close, 20, 2.5)).ShouldContain("2.5");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
            Thread.CurrentThread.CurrentUICulture = originalUi;
        }
    }

    /// <summary>A mixed frame: bar features on four timeframes, the day lane, and every date-driven feature, with a calendar, a context covering the bars and some expiry dates.</summary>
    private static (List<FeatureSpec> Specs, FrameOptions Options) DateDrivenFrame(List<Bar> bars, int seed)
    {
        List<FeatureSpec> specs =
        [
            new("sma", Timeframe.M1, PriceSource.Close, 5),
            new("rsi", Timeframe.M5, PriceSource.Close, 3),
            new("close", Timeframe.D1),
            new("vwap"),
            .. FrameContextFixtures.Specs(TestSpecs.ContextNames),
        ];
        var expiry = new HashSet<DateOnly>(FrameContextFixtures.Dates(new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31)).Where(d => d.DayOfWeek == DayOfWeek.Thursday));
        return (specs, new FrameOptions(CalendarFixtures.For(bars), FrameOptions.DefaultHistoryDepth, FrameContextFixtures.Build(FrameContextFixtures.Covering(bars, seed)), expiry));
    }

    [Fact]
    public void Two_runs_with_the_calendar_and_market_context_features_give_the_same_bits_also_on_another_thread()
    {
        var bars = SeriesGenerator.Random(51, 6_000, 9_000, specialSessions: true);
        var (specs, options) = DateDrivenFrame(bars, 51);

        var first = Bits(bars, specs, options);
        var second = Bits(bars, specs, options);
        List<long>? third = null;
        var thread = new Thread(() => third = Bits(bars, specs, options));
        thread.Start();
        thread.Join();

        second.ShouldBe(first);
        third.ShouldNotBeNull().ShouldBe(first);
        first.Distinct().Count().ShouldBeGreaterThan(1_000, "the run produced many different values, not a row of NaN");
    }

    [Fact]
    public void A_frame_with_the_calendar_and_market_context_features_that_was_reset_equals_a_new_one()
    {
        var earlier = SeriesGenerator.Random(53, 3_000, 4_000, specialSessions: true);
        var bars = SeriesGenerator.Random(52, 6_000, 9_000, specialSessions: true);
        var (specs, options) = DateDrivenFrame(bars, 52);
        var used = FeatureFrame.Create(specs, options).Value;
        foreach (var bar in earlier)
        {
            used.Step(bar);
        }

        used.Reset();
        var fresh = FeatureFrame.Create(specs, options).Value;
        foreach (var bar in bars)
        {
            used.Step(bar);
            fresh.Step(bar);
            for (var h = 0; h < specs.Count; h++)
            {
                if (!Tolerance.Identical(used[h], fresh[h]) || !Tolerance.Identical(used.At(h, 1), fresh.At(h, 1)) || !Tolerance.Identical(used.At(h, 7), fresh.At(h, 7)))
                {
                    Assert.Fail($"{TestSpecs.Describe(specs[h])} differs after Reset at {bar.Start:u}");
                }
            }
        }

        (used.Steps, used.IgnoredBars).ShouldBe((fresh.Steps, fresh.IgnoredBars));
    }

    [Fact]
    public void The_calendar_and_market_context_features_and_their_messages_do_not_depend_on_the_current_culture()
    {
        var bars = SeriesGenerator.Random(54, 4_000, 6_000, specialSessions: true);
        var (specs, options) = DateDrivenFrame(bars, 54);
        var expected = Bits(bars, specs, options);
        var invalid = new[] { new FeatureSpec("regime"), new FeatureSpec("is_expiry_day"), new FeatureSpec("vix", Timeframe.D1), new FeatureSpec("breadth", Timeframe.M1, PriceSource.Close, 2.5) };
        var expectedMessages = FeatureFrame.Create(invalid).Error.Fields!.Select(f => f.Message).ToList();
        var comma = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";
        comma.NumberFormat.NegativeSign = "~";
        var original = Thread.CurrentThread.CurrentCulture;
        var originalUi = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = comma;
            Thread.CurrentThread.CurrentUICulture = comma;

            Bits(bars, specs, options).ShouldBe(expected);
            FeatureFrame.Create(invalid).Error.Fields!.Select(f => f.Message).ShouldBe(expectedMessages);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
            Thread.CurrentThread.CurrentUICulture = originalUi;
        }

        expectedMessages.Count.ShouldBe(4);
    }
}
