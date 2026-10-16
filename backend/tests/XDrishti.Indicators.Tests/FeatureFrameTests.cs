using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>Creating a frame: sharing, requested features only, validation before any bar, handles and TryGet.</summary>
public sealed class FeatureFrameTests
{
    private static FeatureFrame Create(params FeatureSpec[] specs) => FeatureFrame.Create(specs).Value;

    private static Error Refused(params FeatureSpec[] specs)
    {
        var result = FeatureFrame.Create(specs);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(FeatureFrame.InvalidCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        return result.Error;
    }

    [Fact]
    public void Equal_specs_share_one_indicator_and_keep_their_own_handles()
    {
        var sma = new FeatureSpec("sma", Timeframe.M15, PriceSource.Close, 20);

        var frame = Create(sma, new FeatureSpec("sma", Timeframe.M15, PriceSource.Close, 20), new FeatureSpec("sma", Timeframe.M15, PriceSource.Close, 21));

        frame.Count.ShouldBe(3);
        frame.InstanceCount.ShouldBe(2);
        frame.BuilderCount.ShouldBe(1);
    }

    [Fact]
    public void A_source_the_indicator_does_not_read_does_not_make_a_second_feature()
    {
        var frame = Create(new FeatureSpec("atr", Timeframe.M1, PriceSource.Close, 14), new FeatureSpec("atr", Timeframe.M1, PriceSource.High, 14), new FeatureSpec("sma", Timeframe.M1, PriceSource.High, 14), new FeatureSpec("sma", Timeframe.M1, PriceSource.Low, 14));

        frame.InstanceCount.ShouldBe(3);
        frame.HandleOf(new FeatureSpec("atr", Timeframe.M1, PriceSource.Open, 14)).ShouldBe(0);
    }

    [Fact]
    public void Only_the_requested_timeframes_get_a_builder_and_only_the_requested_features_run()
    {
        Create(new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 5), new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, 5)).BuilderCount.ShouldBe(0);

        var frame = Create(new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 5), new FeatureSpec("rsi", Timeframe.D1, PriceSource.Close, 14), new FeatureSpec("sma", Timeframe.H1, PriceSource.Close, 5));

        frame.BuilderCount.ShouldBe(3);
        frame.InstanceCount.ShouldBe(3);
    }

    [Fact]
    public void The_weekly_and_monthly_timeframes_get_a_builder_each_and_equal_specs_on_them_share_one_instance()
    {
        var frame = Create(
            new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 10),
            new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 10),
            new FeatureSpec("close", Timeframe.W1),
            new FeatureSpec("sma", Timeframe.Mo1, PriceSource.Close, 10),
            new FeatureSpec("close", Timeframe.M1));

        frame.Count.ShouldBe(5);
        frame.BuilderCount.ShouldBe(2, "one for 1W and one for 1M; 1m needs none");
        frame.InstanceCount.ShouldBe(4);
        frame.HasTimeframe(Timeframe.W1).ShouldBeTrue();
        frame.HasTimeframe(Timeframe.Mo1).ShouldBeTrue();
        frame.HasTimeframe(Timeframe.D1).ShouldBeFalse("a weekly feature does not make a daily lane");
        frame.HandleOf(new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 10)).ShouldBe(0);
        frame.HandleOf(new FeatureSpec("sma", Timeframe.Mo1, PriceSource.Close, 10)).ShouldBe(3);
        frame.WarmUp(0).ShouldBe(10);
    }

    [Fact]
    public void Handles_read_the_value_and_readiness_and_the_spec_finds_its_handle()
    {
        var sma = new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 2);
        var highest = new FeatureSpec("highest", Timeframe.M1, PriceSource.High, 3);
        var frame = Create(sma, highest);
        var bars = Minutes.Closes(new DateOnly(2026, 3, 2), 10, 20, 30);

        frame.Step(bars[0]);
        frame.IsReady(0).ShouldBeFalse();
        frame[0].ShouldBeNaN();
        frame.TryGet(sma, out var early).ShouldBeFalse();
        early.ShouldBeNaN();

        frame.Step(bars[1]);
        frame.Step(bars[2]);
        frame.IsReady(0).ShouldBeTrue();
        frame[0].ShouldBe(25.0);
        frame[1].ShouldBe(30.0);
        frame.TryGet(sma, out var value).ShouldBeTrue();
        value.ShouldBe(25.0);
        frame.HandleOf(highest).ShouldBe(1);
        frame.SpecOf(1).ShouldBe(highest);
        frame.WarmUp(1).ShouldBe(3);
        frame.Steps.ShouldBe(3);
        frame.TryGet(new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 99), out _).ShouldBeFalse();
        frame.HandleOf(new FeatureSpec("nope")).ShouldBe(-1);
    }

    [Fact]
    public void Incomplete_higher_timeframe_bars_are_consumed_and_counted()
    {
        var frame = Create(new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 1));
        var day = new DateOnly(2026, 3, 2);

        // 09:15 to 09:18 (09:19 missing), then a full second bucket
        foreach (var minute in new[] { 555, 556, 557, 558, 560, 561, 562, 563, 564 })
        {
            frame.Step(Minutes.Close(day, minute, minute));
        }

        frame.IncompleteBarsConsumed.ShouldBe(1);
        frame[0].ShouldBe(564.0);    // the second bucket closed at 09:24 with its last close
    }

    [Fact]
    public void A_week_with_a_half_missing_day_is_consumed_incomplete_and_counted_once_and_the_clean_week_is_not()
    {
        var series = BarFixtures.GetPeriods("half missing day");
        var frame = Create(new FeatureSpec("close", Timeframe.W1), new FeatureSpec("close", Timeframe.Mo1));

        foreach (var bar in series.Bars)
        {
            frame.Step(bar);
        }

        // the week of 2 March (a Wednesday with every second minute missing) closes with the first bar of 9 March, the clean week of the 9th with the first bar of the 16th; March is still open
        frame.IncompleteBarsConsumed.ShouldBe(1);
        frame.TryLastClosed(Timeframe.W1, out var lastWeek).ShouldBeTrue();
        (lastWeek.Complete, SessionClock.DateOf(lastWeek.StartEpochSeconds)).ShouldBe((true, BarFixtures.Mon.AddDays(7)));
        frame.IsReady(1).ShouldBeFalse("no month has closed");
    }

    [Fact]
    public void Reset_returns_the_frame_to_its_first_state()
    {
        var specs = new[] { new FeatureSpec("sma", Timeframe.M5, PriceSource.Close, 2), new FeatureSpec("vwap", Timeframe.M1) };
        var frame = Create(specs);
        foreach (var bar in BarFixtures.Get("normal day"))
        {
            frame.Step(bar);
        }

        frame.Reset();

        frame.Steps.ShouldBe(0);
        frame.IncompleteBarsConsumed.ShouldBe(0);
        frame.IsReady(0).ShouldBeFalse();
        frame[1].ShouldBeNaN();
    }

    [Fact]
    public void An_unknown_indicator_is_refused_with_the_field_of_the_spec_before_any_bar_is_fed()
    {
        var error = Refused(new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 5), new FeatureSpec("macd", Timeframe.M1, PriceSource.Close, 5));

        error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldContain("specs[1].name");
        error.Fields!.Single().Message.ShouldContain("macd");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.5)]
    [InlineData(10_001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_period_outside_its_range_or_not_whole_is_refused(double period)
    {
        var error = Refused(new FeatureSpec("ema", Timeframe.M1, PriceSource.Close, period));

        error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("specs[0].p1");
    }

    [Fact]
    public void A_parameter_the_indicator_does_not_have_must_be_zero()
    {
        var error = Refused(new FeatureSpec("vwap", Timeframe.M1, PriceSource.Close, 3), new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3, 1));

        error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["specs[0].p1", "specs[1].p2"]);
    }

    [Theory]
    [InlineData((Timeframe)99)]
    [InlineData((Timeframe)(-1))]
    public void A_timeframe_outside_the_supported_set_is_refused(Timeframe timeframe)
    {
        var error = Refused(new FeatureSpec("sma", timeframe, PriceSource.Close, 5));

        error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("specs[0].timeframe");
        error.Fields![0].Message.ShouldBe("The timeframe must be one of 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W, 1M.");
    }

    [Theory]
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    public void The_weekly_and_monthly_timeframes_are_accepted_for_a_continuous_indicator(Timeframe timeframe)
    {
        var result = FeatureFrame.Create([new FeatureSpec("sma", timeframe, PriceSource.Close, 5), new FeatureSpec("rsi", timeframe, PriceSource.Close, 14), new FeatureSpec("close", timeframe)]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasTimeframe(timeframe).ShouldBeTrue();
        result.Value.SpecOf(0).Timeframe.ShouldBe(timeframe);
    }

    [Fact]
    public void Intraday_only_indicators_are_refused_on_daily_bars_and_an_opening_range_must_be_whole_bars()
    {
        var error = Refused(
            new FeatureSpec("vwap", Timeframe.D1),
            new FeatureSpec("or_high", Timeframe.D1, PriceSource.Close, 15),
            new FeatureSpec("or_low", Timeframe.M15, PriceSource.Close, 20),
            new FeatureSpec("or_high", Timeframe.M15, PriceSource.Close, 30));

        error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["specs[0].timeframe", "specs[1].timeframe", "specs[2].p1"]);
    }

    [Fact]
    public void Intraday_and_session_bound_indicators_are_refused_on_weekly_and_monthly_bars_and_each_problem_is_reported_at_its_spec()
    {
        var error = Refused(
            new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 5),
            new FeatureSpec("vwap", Timeframe.W1),
            new FeatureSpec("or_low", Timeframe.Mo1, PriceSource.Close, 15),
            new FeatureSpec("prev_day_high", Timeframe.Mo1),
            new FeatureSpec("today_open", Timeframe.W1),
            new FeatureSpec("gap_pct", Timeframe.D1));

        error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["specs[1].timeframe", "specs[2].timeframe", "specs[3].timeframe", "specs[4].timeframe"]);
        foreach (var (field, name, code) in new[] { (0, "vwap", "1W"), (1, "or_low", "1M"), (2, "prev_day_high", "1M"), (3, "today_open", "1W") })
        {
            error.Fields![field].Message.ShouldContain($"'{name}'");
            error.Fields![field].Message.ShouldContain(code);
        }
    }

    [Fact]
    public void An_unknown_price_source_and_an_empty_list_are_refused_and_every_problem_is_reported_at_once()
    {
        Refused(new FeatureSpec("sma", Timeframe.M1, (PriceSource)9, 5)).Fields.ShouldNotBeNull().Single().Field.ShouldBe("specs[0].source");
        FeatureFrame.Create([]).Error.Code.ShouldBe(FeatureFrame.InvalidCode);
        Refused(new FeatureSpec("zzz"), new FeatureSpec("sma", (Timeframe)99, PriceSource.Close, 0)).Fields.ShouldNotBeNull().Count.ShouldBe(3);
    }

    [Fact]
    public void Every_catalog_indicator_with_its_defaults_makes_a_valid_frame_on_every_supported_timeframe()
    {
        foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
        {
            var specs = IndicatorCatalog.All.Where(i => i.Supports(timeframe)).Select(i => i.DefaultSpec(timeframe)).ToList();
            var specsWithMinutes = specs.Select(s => s.Name.StartsWith("or_", StringComparison.Ordinal) ? s with { P1 = Math.Max(30, timeframe.Minutes()) } : s).ToList();

            FeatureFrame.Create(specsWithMinutes, new FrameOptions(Context: MarketContext.Create([]).Value, ExpiryDays: new HashSet<DateOnly>())).IsSuccess.ShouldBeTrue(timeframe.ToCode());
        }
    }

    // ---- the Default source (ADR 0032) ----------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_default_source_resolves_to_the_catalogs_default_for_each_indicator()
    {
        var frame = Create(new FeatureSpec("highest", P1: 20), new FeatureSpec("lowest", P1: 20), new FeatureSpec("sma", P1: 20), new FeatureSpec("roc", P1: 5));

        frame.SpecOf(0).Source.ShouldBe(PriceSource.High);
        frame.SpecOf(1).Source.ShouldBe(PriceSource.Low);
        frame.SpecOf(2).Source.ShouldBe(PriceSource.Close);
        frame.SpecOf(3).Source.ShouldBe(PriceSource.Close);
    }

    [Fact]
    public void A_spec_with_the_default_source_means_what_the_catalogs_default_spec_means()
    {
        IndicatorCatalog.TryGet("highest", out var highest).ShouldBeTrue();
        IndicatorCatalog.TryGet("lowest", out var lowest).ShouldBeTrue();
        var frame = Create(new FeatureSpec("highest", P1: 3), new FeatureSpec("lowest", P1: 3));

        frame.HandleOf(highest.DefaultSpec() with { P1 = 3 }).ShouldBe(0);
        frame.HandleOf(lowest.DefaultSpec() with { P1 = 3 }).ShouldBe(1);
        highest.DefaultSpec().Source.ShouldBe(PriceSource.High);

        var bars = new[]
        {
            Minutes.Bar(new DateOnly(2026, 3, 2), Minutes.Open, 10, 50, 1, 12),
            Minutes.Bar(new DateOnly(2026, 3, 2), Minutes.Open + 1, 10, 40, 2, 11),
            Minutes.Bar(new DateOnly(2026, 3, 2), Minutes.Open + 2, 10, 30, 3, 10),
        };
        foreach (var bar in bars)
        {
            frame.Step(bar);
        }

        frame[0].ShouldBe(50.0, "the highest of the highs, not of the closes");
        frame[1].ShouldBe(1.0, "the lowest of the lows");
    }

    [Fact]
    public void A_default_source_spec_and_the_explicit_default_source_share_one_instance_but_another_source_does_not()
    {
        var frame = Create(
            new FeatureSpec("highest", P1: 20),
            new FeatureSpec("highest", Timeframe.M1, PriceSource.High, 20),
            new FeatureSpec("highest", Timeframe.M1, PriceSource.Close, 20),
            new FeatureSpec("sma", P1: 5),
            new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 5));

        frame.Count.ShouldBe(5);
        frame.InstanceCount.ShouldBe(3);
        frame.HandleOf(new FeatureSpec("highest", P1: 20)).ShouldBe(0);
        frame.HandleOf(new FeatureSpec("highest", Timeframe.M1, PriceSource.High, 20)).ShouldBe(0);
        frame.HandleOf(new FeatureSpec("highest", Timeframe.M1, PriceSource.Close, 20)).ShouldBe(2);
        frame.HandleOf(new FeatureSpec("sma", P1: 5)).ShouldBe(3);
    }

    [Fact]
    public void The_catalog_creates_the_same_indicator_for_the_default_source_as_for_the_explicit_one()
    {
        IndicatorCatalog.TryGet("highest", out var info).ShouldBeTrue();
        var viaDefault = info.Create(new FeatureSpec("highest", P1: 2));
        var viaHigh = info.Create(new FeatureSpec("highest", Timeframe.M1, PriceSource.High, 2));
        var bar = Minutes.Bar(new DateOnly(2026, 3, 2), Minutes.Open, 10, 50, 1, 12);

        viaDefault.Update(bar);
        viaHigh.Update(bar);
        viaDefault.Update(bar);
        viaHigh.Update(bar);

        viaDefault.Value.ShouldBe(50.0);
        viaHigh.Value.ShouldBe(50.0);
    }

    [Fact]
    public void A_bare_spec_has_the_default_source_the_zero_value()
    {
        new FeatureSpec("sma").Source.ShouldBe(PriceSource.Default);
        ((int)PriceSource.Default).ShouldBe(0);
        default(PriceSource).ShouldBe(PriceSource.Default);
    }

    // ---- parameter combinations of the new indicators -----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Macd_with_a_fast_period_not_below_the_slow_one_is_refused_on_the_fast_parameter()
    {
        var error = Refused(
            new FeatureSpec("macd_line", Timeframe.M1, PriceSource.Close, 26, 26, 9),
            new FeatureSpec("macd_signal", Timeframe.M1, PriceSource.Close, 30, 26, 9),
            new FeatureSpec("macd_hist", Timeframe.M1, PriceSource.Close, 12, 26, 9));

        error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["specs[0].p1", "specs[1].p1"]);
        error.Fields![0].Message.ShouldContain("fast");
    }

    [Theory]
    [InlineData("bb_upper", 0)]
    [InlineData("bb_middle", -1)]
    [InlineData("bb_lower", 0)]
    [InlineData("bb_width", -2.5)]
    public void Bollinger_bands_with_k_not_above_zero_are_refused_on_the_k_parameter(string name, double k)
    {
        var error = Refused(new FeatureSpec(name, Timeframe.M1, PriceSource.Close, 20, k));

        error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("specs[0].p2");
    }

    [Theory]
    [InlineData("supertrend", 0)]
    [InlineData("supertrend_dir", -3)]
    public void Supertrend_with_a_multiplier_not_above_zero_is_refused_on_the_multiplier(string name, double multiplier)
    {
        var error = Refused(new FeatureSpec(name, Timeframe.M1, PriceSource.Close, 10, multiplier));

        error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("specs[0].p2");
    }

    [Theory]
    [InlineData("nr", 1)]
    [InlineData("nr", 0)]
    [InlineData("pivot_high", 0)]
    [InlineData("pivot_low", 0)]
    [InlineData("pivot_high_age", 0)]
    [InlineData("pivot_low_age", -1)]
    public void Nr_below_two_and_a_pivot_period_below_one_are_refused(string name, double period)
    {
        var error = Refused(new FeatureSpec(name, Timeframe.M1, PriceSource.Close, period));

        error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("specs[0].p1");
    }

    [Fact]
    public void Parameters_that_are_not_finite_or_not_whole_are_refused_for_the_new_indicators_and_extra_ones_must_be_zero()
    {
        var error = Refused(
            new FeatureSpec("bb_upper", Timeframe.M1, PriceSource.Close, 20, double.NaN),
            new FeatureSpec("supertrend", Timeframe.M1, PriceSource.Close, 10.5, 3),
            new FeatureSpec("inside_bar", Timeframe.M1, PriceSource.Close, 3),
            new FeatureSpec("macd_line", Timeframe.M1, PriceSource.Close, 12, 26, 9, 1),
            new FeatureSpec("adx", Timeframe.M1, PriceSource.Close, 14, 2));

        error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["specs[0].p2", "specs[1].p1", "specs[2].p1", "specs[3].p4", "specs[4].p2"]);
    }

    [Fact]
    public void Every_valid_combination_of_the_new_indicators_makes_a_frame_and_the_old_create_overload_with_a_calendar_still_works()
    {
        var specs = new[]
        {
            new FeatureSpec("macd_hist", Timeframe.M15, PriceSource.Close, 12, 26, 9),
            new FeatureSpec("bb_width", Timeframe.H1, PriceSource.Close, 20, 2),
            new FeatureSpec("supertrend_dir", Timeframe.D1, PriceSource.Close, 10, 3),
            new FeatureSpec("pivot_low_age", Timeframe.M5, PriceSource.Close, 3),
        };

        FeatureFrame.Create(specs).IsSuccess.ShouldBeTrue();
        var withCalendar = FeatureFrame.Create(specs, new XDrishti.Domain.Market.Calendar.TradingCalendar());
        withCalendar.IsSuccess.ShouldBeTrue();
        withCalendar.Value.HistoryDepth.ShouldBe(8);
    }
}
