using System.Collections.Frozen;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// Building a frame with the calendar and market-context features (P3-01c criteria 6, 7 and 10): what <c>Create</c> accepts and refuses, with the field and the text of each error, and how the date lane sits next
/// to the lanes of the other timeframes.
/// </summary>
public sealed class FrameContextCreateTests
{
    private static readonly IReadOnlyList<string> AllNames = TestSpecs.ContextNames;

    private static readonly MarketContext SmallContext = FrameContextFixtures.Build(FrameContextFixtures.Consecutive(1, 30), FrameContextFixtures.Small);

    private static FrameOptions Full => new(Context: SmallContext, ExpiryDays: new HashSet<DateOnly>());

    private static Error Refused(FrameOptions? options, params FeatureSpec[] specs)
    {
        var result = FeatureFrame.Create(specs, options);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(FeatureFrame.InvalidCode);
        return result.Error;
    }

    [Theory]
    [InlineData("index_trend")]
    [InlineData("index_adx")]
    [InlineData("index_vol_pct")]
    [InlineData("vix")]
    [InlineData("vix_pct")]
    [InlineData("vix_chg")]
    [InlineData("breadth")]
    [InlineData("regime_trend")]
    [InlineData("regime_vol")]
    [InlineData("regime")]
    public void A_context_feature_without_a_context_is_refused_on_options_context_and_the_message_names_it(string name)
    {
        foreach (var options in new FrameOptions?[] { null, new FrameOptions(), new FrameOptions(ExpiryDays: new HashSet<DateOnly>()) })
        {
            var error = Refused(options, new FeatureSpec(name));

            var field = error.Fields.ShouldNotBeNull().Single();
            field.Field.ShouldBe("options.context");
            field.Message.ShouldBe($"The market context is needed by '{name}'; give FrameOptions.Context.");
        }
    }

    [Fact]
    public void The_expiry_day_without_expiry_dates_is_refused_on_options_expiry_days_and_an_empty_set_is_a_valid_set()
    {
        var error = Refused(new FrameOptions(Context: SmallContext), new FeatureSpec("is_expiry_day"));

        var field = error.Fields.ShouldNotBeNull().Single();
        field.Field.ShouldBe("options.expiryDays");
        field.Message.ShouldBe("The expiry dates are needed by 'is_expiry_day'; give FrameOptions.ExpiryDays (an empty set is allowed).");
        FeatureFrame.Create([new FeatureSpec("is_expiry_day")], new FrameOptions(ExpiryDays: new HashSet<DateOnly>())).IsSuccess.ShouldBeTrue();
        FeatureFrame.Create([new FeatureSpec("is_expiry_day")], new FrameOptions(ExpiryDays: new HashSet<DateOnly> { new(2026, 3, 3) })).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_weekday_needs_neither_a_context_nor_expiry_dates_and_the_expiry_day_does_not_need_a_context()
    {
        FeatureFrame.Create([new FeatureSpec("day_of_week")]).IsSuccess.ShouldBeTrue();
        FeatureFrame.Create([new FeatureSpec("day_of_week")], new FrameOptions()).IsSuccess.ShouldBeTrue();
        FeatureFrame.Create([new FeatureSpec("is_expiry_day"), new FeatureSpec("day_of_week")], new FrameOptions(ExpiryDays: new HashSet<DateOnly>())).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Every_missing_requirement_is_reported_once_with_the_other_problems_of_the_specs()
    {
        var error = Refused(
            new FrameOptions(HistoryDepth: 0),
            new FeatureSpec("vix"),
            new FeatureSpec("regime"),
            new FeatureSpec("vix"),
            new FeatureSpec("is_expiry_day"),
            new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 0),
            new FeatureSpec("is_expiry_day", Timeframe.M5));

        error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["options.historyDepth", "specs[4].p1", "specs[5].timeframe", "options.context", "options.expiryDays"]);
        error.Fields![3].Message.ShouldBe("The market context is needed by 'vix', 'regime'; give FrameOptions.Context.");
        error.Fields![4].Message.ShouldBe("The expiry dates are needed by 'is_expiry_day'; give FrameOptions.ExpiryDays (an empty set is allowed).");
    }

    [Fact]
    public void A_frame_asked_for_every_name_with_both_options_is_valid_and_a_frame_without_them_lists_every_name_that_needs_them()
    {
        var all = FrameContextFixtures.Specs(AllNames);

        FeatureFrame.Create(all, Full).IsSuccess.ShouldBeTrue();
        var error = Refused(new FrameOptions(), all);
        error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["options.context", "options.expiryDays"]);
        error.Fields![0].Message.ShouldBe("The market context is needed by 'index_trend', 'index_adx', 'index_vol_pct', 'vix', 'vix_pct', 'vix_chg', 'breadth', 'regime_trend', 'regime_vol', 'regime'; give FrameOptions.Context.");
    }

    [Fact]
    public void A_date_driven_feature_on_another_timeframe_than_1m_is_refused_naming_the_feature_and_the_timeframe()
    {
        foreach (var name in AllNames)
        {
            foreach (var timeframe in IndicatorCatalog.SupportedTimeframes.Where(t => t != Timeframe.M1))
            {
                var error = Refused(Full, new FeatureSpec(name, timeframe));

                var field = error.Fields.ShouldNotBeNull().Single();
                field.Field.ShouldBe("specs[0].timeframe", $"{name} on {timeframe.ToCode()}");
                field.Message.ShouldBe($"'{name}' changes once per date; it can only be asked for on 1m (the default), not on {timeframe.ToCode()}.");
            }
        }
    }

    [Fact]
    public void An_unknown_timeframe_is_refused_with_the_general_message_not_the_date_one()
    {
        var error = Refused(Full, new FeatureSpec("regime", (Timeframe)99));

        error.Fields.ShouldNotBeNull().Single().Message.ShouldBe("The timeframe must be one of 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W, 1M.");
    }

    [Fact]
    public void A_date_driven_feature_takes_no_parameters()
    {
        foreach (var name in AllNames)
        {
            var error = Refused(Full, new FeatureSpec(name, Timeframe.M1, PriceSource.Close, 3, 0, 0, 2));

            error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["specs[0].p1", "specs[0].p4"], name);
            error.Fields![0].Message.ShouldBe($"'{name}' takes 0 parameter(s); the rest must be 0.");
        }
    }

    [Fact]
    public void The_source_of_a_date_driven_spec_is_ignored_so_two_specs_for_one_name_are_one_feature()
    {
        var frame = FeatureFrame.Create(
            [new FeatureSpec("vix"), new FeatureSpec("vix", Timeframe.M1, PriceSource.Close), new FeatureSpec("vix", Timeframe.M1, PriceSource.High), new FeatureSpec("regime"), new FeatureSpec("regime")],
            Full).Value;

        frame.Count.ShouldBe(5);
        frame.InstanceCount.ShouldBe(2);
        frame.HandleOf(new FeatureSpec("vix", Timeframe.M1, PriceSource.Low)).ShouldBe(0);
        frame.HandleOf(new FeatureSpec("regime")).ShouldBe(3);
        frame.SpecOf(2).Source.ShouldBe(PriceSource.Close);
    }

    [Fact]
    public void Date_driven_and_bar_features_share_one_frame_with_a_handle_each_and_the_instances_are_counted_once()
    {
        var frame = FeatureFrame.Create(
            [new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3), new FeatureSpec("vix"), new FeatureSpec("ema", Timeframe.M5, PriceSource.Close, 3), new FeatureSpec("day_of_week"), new FeatureSpec("vix")],
            Full).Value;

        frame.Count.ShouldBe(5);
        frame.InstanceCount.ShouldBe(4);
        frame.BuilderCount.ShouldBe(1);
        frame.HasTimeframe(Timeframe.M1).ShouldBeTrue();
        frame.HasTimeframe(Timeframe.M5).ShouldBeTrue();
        frame.HasTimeframe(Timeframe.D1).ShouldBeFalse();
        frame.WarmUp(0).ShouldBe(3);
        frame.WarmUp(3).ShouldBe(1);
    }

    [Fact]
    public void A_frame_of_date_features_alone_still_has_a_one_minute_lane_with_its_closed_flags()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("day_of_week"), new FeatureSpec("vix")], Full).Value;
        var bars = FrameContextFixtures.OnDates([new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3)], Minutes.Open, Minutes.Open + 1);

        frame.HasTimeframe(Timeframe.M1).ShouldBeTrue();
        frame.BarClosed(Timeframe.M1).ShouldBeFalse("before the first step");
        frame.BuilderCount.ShouldBe(0);
        foreach (var bar in bars)
        {
            frame.Step(bar);

            frame.BarClosed(Timeframe.M1).ShouldBeTrue();
            frame.ClosedBarsAt(Timeframe.M1).ShouldBe(1);
            frame.TryLastClosed(Timeframe.M1, out var last).ShouldBeTrue();
            last.Bar.ShouldBe(bar);
        }

        frame.Steps.ShouldBe(4);
        Should.Throw<ArgumentOutOfRangeException>(() => frame.BarClosed(Timeframe.D1));
    }

    [Fact]
    public void A_frame_without_a_date_feature_has_no_one_minute_lane_unless_a_feature_is_on_it()
    {
        var frame = FeatureFrame.Create([new FeatureSpec("ema", Timeframe.M5, PriceSource.Close, 3)], Full).Value;

        frame.HasTimeframe(Timeframe.M1).ShouldBeFalse();
        frame.InstanceCount.ShouldBe(1);
    }

    [Fact]
    public void The_options_keep_their_positional_order_and_defaults_and_the_calendar_overload_still_works()
    {
        var calendar = new TradingCalendar();
        var expiry = new HashSet<DateOnly>();

        var plain = new FrameOptions();
        var positional = new FrameOptions(calendar, 4, SmallContext, expiry);

        plain.Calendar.ShouldBeNull();
        plain.HistoryDepth.ShouldBe(8);
        plain.Context.ShouldBeNull();
        plain.ExpiryDays.ShouldBeNull();
        (positional.Calendar, positional.HistoryDepth, positional.Context, positional.ExpiryDays).ShouldBe((calendar, 4, SmallContext, expiry));
        new FrameOptions(calendar).HistoryDepth.ShouldBe(8);
        new FrameOptions(calendar, 3).Context.ShouldBeNull();
        (plain with { Context = SmallContext }).Context.ShouldBeSameAs(SmallContext);
        FeatureFrame.Create([new FeatureSpec("day_of_week"), new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3)], calendar).Value.HistoryDepth.ShouldBe(8);
        FeatureFrame.Create([new FeatureSpec("vix")], calendar).IsFailure.ShouldBeTrue("the calendar overload has no context to give");
    }

    [Fact]
    public void The_frame_takes_its_own_copy_of_the_expiry_dates()
    {
        var tuesday = new DateOnly(2026, 3, 3);
        var wednesday = new DateOnly(2026, 3, 4);
        var set = new HashSet<DateOnly> { tuesday };
        var frame = FeatureFrame.Create([new FeatureSpec("is_expiry_day")], new FrameOptions(ExpiryDays: set)).Value;

        set.Remove(tuesday);
        set.Add(wednesday);
        frame.Step(Minutes.Close(tuesday, Minutes.Open, 100));
        var onTuesday = frame[0];
        frame.Step(Minutes.Close(wednesday, Minutes.Open, 100));
        var onWednesday = frame[0];

        onTuesday.ShouldBe(1.0, "the set at the time of Create");
        onWednesday.ShouldBe(0.0, "a date added afterwards is not an expiry day of this frame");
    }

    [Fact]
    public void A_frozen_set_of_expiry_dates_is_read_as_given()
    {
        var tuesday = new DateOnly(2026, 3, 3);
        var frame = FeatureFrame.Create([new FeatureSpec("is_expiry_day")], new FrameOptions(ExpiryDays: new[] { tuesday }.ToFrozenSet())).Value;

        frame.Step(Minutes.Close(tuesday, Minutes.Open, 100));

        frame[0].ShouldBe(1.0);
    }

    [Fact]
    public void The_catalog_entries_of_the_twelve_names_describe_them_as_parameterless_continuous_and_1m_only()
    {
        foreach (var name in AllNames)
        {
            IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue(name);

            info.DateDriven.ShouldBeTrue(name);
            info.Parameters.ShouldBeEmpty(name);
            info.SessionBehaviour.ShouldBe(SessionBehaviour.Continuous, name);
            info.IntradayOnly.ShouldBeFalse(name);
            info.UsesSource.ShouldBeFalse(name);
            info.SupportedTimeframes.ShouldBe([Timeframe.M1], name);
            info.Summary.ShouldNotBeNullOrWhiteSpace();
            info.Create(info.DefaultSpec()).ShouldBeOfType<DateFeature>();
        }

        IndicatorCatalog.All.Where(i => i.DateDriven).Select(i => i.Name).ShouldBe(AllNames);
    }
}
