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
    [InlineData(Timeframe.W1)]
    [InlineData(Timeframe.Mo1)]
    [InlineData((Timeframe)99)]
    public void A_timeframe_outside_the_supported_set_is_refused(Timeframe timeframe)
    {
        var error = Refused(new FeatureSpec("sma", timeframe, PriceSource.Close, 5));

        error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("specs[0].timeframe");
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
    public void An_unknown_price_source_and_an_empty_list_are_refused_and_every_problem_is_reported_at_once()
    {
        Refused(new FeatureSpec("sma", Timeframe.M1, (PriceSource)9, 5)).Fields.ShouldNotBeNull().Single().Field.ShouldBe("specs[0].source");
        FeatureFrame.Create([]).Error.Code.ShouldBe(FeatureFrame.InvalidCode);
        Refused(new FeatureSpec("zzz"), new FeatureSpec("sma", Timeframe.W1, PriceSource.Close, 0)).Fields.ShouldNotBeNull().Count.ShouldBe(3);
    }

    [Fact]
    public void Every_catalog_indicator_with_its_defaults_makes_a_valid_frame_on_every_supported_timeframe()
    {
        foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
        {
            var specs = IndicatorCatalog.All.Where(i => !(i.IntradayOnly && timeframe == Timeframe.D1)).Select(i => i.DefaultSpec(timeframe)).ToList();
            var specsWithMinutes = specs.Select(s => s.Name.StartsWith("or_", StringComparison.Ordinal) ? s with { P1 = Math.Max(30, timeframe.Minutes()) } : s).ToList();

            FeatureFrame.Create(specsWithMinutes).IsSuccess.ShouldBeTrue(timeframe.ToCode());
        }
    }
}
