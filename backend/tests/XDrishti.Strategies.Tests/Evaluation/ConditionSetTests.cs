using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// <c>ConditionSet.Merge</c>: one plan for several conditions (the union of the features without duplicates, the largest history depth, the needs of the frame) with every condition bound separately to the one frame.
/// </summary>
public sealed class ConditionSetTests
{
    private const Timeframe M5 = Timeframe.M5;

    private static CompiledCondition Of(string source, Timeframe timeframe = M5) => Compiling.Condition(source, timeframe, ValidConditionGenerator.Parameters);

    [Fact]
    public void The_features_are_the_union_without_duplicates_in_the_order_they_first_appear()
    {
        var a = Of("close > open and rsi(14) > 55");
        var b = Of("ema(20) > close and rsi(14) < 80");
        var c = Of("vwap > ema(50)");

        var plan = ConditionSet.Merge([a, b, c]);

        plan.Features.ShouldBe([Specs.Close(M5), Specs.Of("open", M5), Specs.Rsi(M5), Specs.Ema(M5), Specs.Of("vwap", M5), Specs.Ema(M5, 50)]);
        plan.Conditions.ShouldBe([a, b, c], "in the order given");
    }

    [Fact]
    public void Two_spellings_of_one_feature_are_one_feature()
    {
        var plan = ConditionSet.Merge([Of("ema() > 0"), Of("ema(20) > 0"), Of("ema(fast_len + 11) > 0"), Of("ema(20, source=close) > 0"), Of("tf(\"5m\").ema(20) > 0")]);

        plan.Features.ShouldBe([Specs.Ema(M5, 20)]);
    }

    [Fact]
    public void The_same_feature_on_different_timeframes_or_with_different_parameters_stays_separate()
    {
        var plan = ConditionSet.Merge([Of("ema(20) > 0"), Of("ema(20) > 0", Timeframe.M15), Of("ema(21) > 0"), Of("ema(20, source=high) > 0")]);

        plan.Features.ShouldBe([Specs.Ema(M5, 20), Specs.Ema(Timeframe.M15, 20), Specs.Ema(M5, 21), Specs.Ema(M5, 20, PriceSource.High)]);
    }

    [Fact]
    public void The_history_depth_is_the_largest_of_the_conditions_and_at_least_one()
    {
        ConditionSet.Merge([Of("close > 0"), Of("close[3] > 0"), Of("rising(close, 6)"), Of("crosses_above(close, open)")]).HistoryDepth.ShouldBe(7);
        ConditionSet.Merge([Of("close > 0")]).HistoryDepth.ShouldBe(1);
        ConditionSet.Merge([Of("time >= \"09:45\"")]).HistoryDepth.ShouldBe(1);
        ConditionSet.Merge([]).HistoryDepth.ShouldBe(1);
    }

    [Fact]
    public void The_plan_needs_a_context_or_expiry_dates_when_any_condition_does()
    {
        var plain = Of("close > 0");
        var context = Of("regime() == 1");
        var expiry = Of("is_expiry_day == 1");

        ConditionSet.Merge([plain]).NeedsContext.ShouldBeFalse();
        ConditionSet.Merge([plain]).NeedsExpiryDays.ShouldBeFalse();
        ConditionSet.Merge([plain, context]).NeedsContext.ShouldBeTrue();
        ConditionSet.Merge([plain, context]).NeedsExpiryDays.ShouldBeFalse();
        ConditionSet.Merge([expiry, plain]).NeedsExpiryDays.ShouldBeTrue();
        ConditionSet.Merge([expiry, plain]).NeedsContext.ShouldBeFalse();
        ConditionSet.Merge([plain, expiry, context]).NeedsContext.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_set_is_a_plan_without_features()
    {
        var plan = ConditionSet.Merge([]);

        plan.Conditions.ShouldBeEmpty();
        plan.Features.ShouldBeEmpty();
        plan.NeedsContext.ShouldBeFalse();
        plan.NeedsExpiryDays.ShouldBeFalse();
        plan.BindAll(Frames.OfClose()).ShouldBeEmpty();
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        Should.Throw<ArgumentNullException>(() => ConditionSet.Merge(null!)).ParamName.ShouldBe("conditions");
        Should.Throw<ArgumentNullException>(() => ConditionSet.Merge([Of("close > 0"), null!])).ParamName.ShouldBe("conditions");
        Should.Throw<ArgumentNullException>(() => ConditionSet.Merge([]).BindAll(null!)).ParamName.ShouldBe("frame");
    }

    [Fact]
    public void The_plan_keeps_its_own_copy_and_cannot_be_changed_through_its_lists()
    {
        var a = Of("close > open");
        var b = Of("rsi(14) > 55");
        var given = new List<CompiledCondition> { a, b };

        var plan = ConditionSet.Merge(given);
        given.Clear();

        plan.Conditions.Count.ShouldBe(2);
        plan.Features.Count.ShouldBe(3);
        Should.Throw<NotSupportedException>(() => ((IList<CompiledCondition>)plan.Conditions).Add(a));
        Should.Throw<NotSupportedException>(() => ((IList<FeatureSpec>)plan.Features).Clear());
        a.Features.Count.ShouldBe(2, "the conditions are not changed");
    }

    [Fact]
    public void Merging_does_not_change_what_a_condition_reads_and_the_same_condition_can_be_in_several_plans()
    {
        var a = Of("close > open");
        var before = a.Features.ToList();

        var one = ConditionSet.Merge([a, Of("rsi(14) > 55")]);
        var two = ConditionSet.Merge([a]);

        a.Features.ShouldBe(before);
        one.Features.Count.ShouldBe(3);
        two.Features.ShouldBe(before);
    }

    [Fact]
    public void Every_condition_bound_to_the_shared_frame_gives_what_it_gives_alone_on_a_frame_of_its_own()
    {
        var sources = new[] { "close > open and rsi(5) > 50", "crosses_above(close, ema(5))", "rising(close, 3)", "between(rsi(5), 30, 70)", "tf(\"15m\").close > tf(\"15m\").open", "time >= \"10:30\" and close > vwap" };
        var conditions = sources.Select(s => Of(s)).ToList();
        var plan = ConditionSet.Merge(conditions);
        var shared = Frames.Of(new FrameOptions { HistoryDepth = plan.HistoryDepth }, [.. plan.Features]);
        var together = plan.BindAll(shared);
        var alone = conditions.Select(c => (Frame: Frames.Of(new FrameOptions { HistoryDepth = c.HistoryDepth }, [.. c.Features, Specs.Close(M5)]), Condition: c)).ToList();
        var bound = alone.Select(a => a.Condition.Bind(a.Frame)).ToList();
        var seen = new HashSet<Truth>();

        foreach (var bar in Series.Sessions(21, 2))
        {
            shared.Step(bar);
            for (var i = 0; i < conditions.Count; i++)
            {
                alone[i].Frame.Step(bar);
                var expected = bound[i].EvaluateTruth(alone[i].Frame);
                together[i].EvaluateTruth(shared).ShouldBe(expected, sources[i]);
                seen.Add(expected);
            }
        }

        seen.ShouldBe([Truth.True, Truth.False, Truth.Unknown], ignoreOrder: true);
        together.Select(t => t.Condition).ShouldBe(conditions, "BindAll keeps the order");
    }

    [Fact]
    public void A_frame_made_from_the_plan_needs_the_context_and_the_expiry_dates_the_plan_says()
    {
        var plan = ConditionSet.Merge([Of("close > 0"), Of("regime() == 1"), Of("is_expiry_day == 1")]);
        var options = new FrameOptions { HistoryDepth = plan.HistoryDepth };

        FeatureFrame.Create(plan.Features, options).IsFailure.ShouldBeTrue("the plan said it needs both");
        FeatureFrame.Create(plan.Features, options with { Context = Contexts.Covering(Clock.Mon, Clock.Fri, 1), ExpiryDays = new HashSet<DateOnly>() }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Binding_all_to_a_frame_that_lacks_a_feature_of_one_condition_throws_for_that_condition()
    {
        var plan = ConditionSet.Merge([Of("close > 0"), Of("rsi(14) > 55")]);
        var frame = Frames.Of(Specs.Close(M5));

        Should.Throw<ArgumentException>(() => plan.BindAll(frame)).Message.ShouldContain("rsi(14)", Case.Sensitive);
    }

    [Fact]
    public void Conditions_compiled_for_different_timeframes_share_one_frame()
    {
        var m5 = Of("close > open", M5);
        var h1 = Of("close > open", Timeframe.H1);
        var plan = ConditionSet.Merge([m5, h1]);
        var frame = Frames.Of(new FrameOptions { HistoryDepth = plan.HistoryDepth }, [.. plan.Features]);
        var bound = plan.BindAll(frame);
        var seenDifferent = false;

        foreach (var bar in Series.Sessions(5, 2))
        {
            frame.Step(bar);
            seenDifferent |= bound[0].EvaluateTruth(frame) != bound[1].EvaluateTruth(frame);
        }

        plan.Features.Count.ShouldBe(4);
        seenDifferent.ShouldBeTrue("a 5-minute bar and a 1-hour bar are not the same candle");
    }
}
