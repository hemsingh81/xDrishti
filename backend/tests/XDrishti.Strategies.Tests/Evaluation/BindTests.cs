using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// <c>CompiledCondition.Bind</c>: it resolves the handles in the frame it is given, throws only for a frame that was not created from the condition's features (or keeps too little history for it), and gives a condition that
/// can be used with every frame of the same layout. The delegate is compiled once per condition, also when several threads bind it at the same moment.
/// </summary>
public sealed class BindTests
{
    private static readonly Timeframe M5 = Timeframe.M5;

    private static IReadOnlyList<Bar> Bars => Series.Sessions(7, 2);

    [Fact]
    public void Handles_are_resolved_in_the_frame_whatever_order_it_lists_the_features_in()
    {
        var condition = Compiling.Condition("close > open and rsi(3) < 90", M5);
        var features = condition.Features.ToList();
        var shuffled = ((FeatureSpec[])[Specs.Of("ema", M5, 4, source: PriceSource.Close), .. features.AsEnumerable().Reverse()]);
        var straight = Frames.Of(new FrameOptions { HistoryDepth = condition.HistoryDepth }, [.. features]);
        var other = Frames.Of(new FrameOptions { HistoryDepth = condition.HistoryDepth }, shuffled);
        var boundStraight = condition.Bind(straight);
        var boundOther = condition.Bind(other);

        straight.HandleOf(features[0]).ShouldNotBe(other.HandleOf(features[0]), "the layouts differ");
        foreach (var bar in Bars)
        {
            straight.Step(bar);
            other.Step(bar);
            boundOther.EvaluateTruth(other).ShouldBe(boundStraight.EvaluateTruth(straight));
        }
    }

    [Fact]
    public void A_frame_without_one_of_the_features_is_refused_with_a_message_that_names_it()
    {
        var condition = Compiling.Condition("close > open and rsi(3) < 90", M5);
        var frame = Frames.Of(Specs.Close(M5), Specs.Of("open", M5));

        var thrown = Should.Throw<ArgumentException>(() => condition.Bind(frame));

        thrown.ParamName.ShouldBe("frame");
        thrown.Message.ShouldContain("close > open and rsi(3) < 90", Case.Sensitive);
        thrown.Message.ShouldContain("rsi(3)", Case.Sensitive);
        thrown.Message.ShouldContain("5m", Case.Sensitive);
    }

    [Fact]
    public void A_frame_with_the_feature_on_another_timeframe_or_with_other_parameters_is_refused()
    {
        var condition = Compiling.Condition("rsi(14) > 55", M5);

        Should.Throw<ArgumentException>(() => condition.Bind(Frames.Of(Specs.Rsi(Timeframe.M15)))).Message.ShouldContain("rsi(14)", Case.Sensitive);
        Should.Throw<ArgumentException>(() => condition.Bind(Frames.Of(Specs.Rsi(M5, 13))));
        Should.Throw<ArgumentException>(() => condition.Bind(Frames.Of(Specs.Rsi(M5, 14, PriceSource.High))), "another source is another feature");
        condition.Bind(Frames.Of(Specs.Rsi(M5, 14))).ShouldNotBeNull();
    }

    [Fact]
    public void A_frame_with_too_little_history_is_refused_with_the_depth_in_the_message()
    {
        var condition = Compiling.Condition("rising(close, 5)", M5);
        condition.HistoryDepth.ShouldBe(6);

        var thrown = Should.Throw<ArgumentException>(() => condition.Bind(Frames.Of(new FrameOptions { HistoryDepth = 5 }, Specs.Close(M5))));

        thrown.ParamName.ShouldBe("frame");
        thrown.Message.ShouldContain("6", Case.Sensitive);
        thrown.Message.ShouldContain("keeps 5", Case.Sensitive);
        condition.Bind(Frames.Of(new FrameOptions { HistoryDepth = 6 }, Specs.Close(M5))).ShouldNotBeNull();
        condition.Bind(Frames.Of(new FrameOptions { HistoryDepth = 256 }, Specs.Close(M5))).ShouldNotBeNull("more history than needed is fine");
    }

    [Fact]
    public void The_deepest_condition_the_lint_accepts_binds_to_a_frame_of_the_largest_depth_and_reads_without_throwing()
    {
        var condition = Compiling.Condition("close[255] > 0 and rising(close, 255)", Timeframe.M1);
        condition.HistoryDepth.ShouldBe(256);
        var frame = Frames.Of(new FrameOptions { HistoryDepth = 256 }, Specs.Close(Timeframe.M1));
        var bound = condition.Bind(frame);

        foreach (var bar in Series.Sessions(3, 1))
        {
            frame.Step(bar);
            bound.EvaluateTruth(frame);
        }

        bound.EvaluateTruth(frame).ShouldNotBe(Truth.Unknown, "375 bars are more than the 256 the condition looks back");
    }

    [Fact]
    public void A_condition_that_reads_no_feature_binds_to_any_frame()
    {
        var condition = Compiling.Condition("time >= \"09:45\"", M5);

        condition.Features.ShouldBeEmpty();
        condition.Bind(Frames.OfClose()).ShouldNotBeNull();
        condition.Bind(Frames.Of(Specs.Rsi(Timeframe.D1))).ShouldNotBeNull();
    }

    [Fact]
    public void Null_arguments_throw_argument_null_exceptions_not_null_references()
    {
        var condition = Compiling.Condition("close > 0", M5);
        var frame = Frames.Of(Specs.Close(M5));
        var bound = condition.Bind(frame);

        Should.Throw<ArgumentNullException>(() => condition.Bind(null!)).ParamName.ShouldBe("frame");
        Should.Throw<ArgumentNullException>(() => bound.Evaluate(null!)).ParamName.ShouldBe("frame");
        Should.Throw<ArgumentNullException>(() => bound.EvaluateTruth(null!)).ParamName.ShouldBe("frame");
    }

    [Fact]
    public void A_bound_condition_can_be_used_with_every_frame_of_the_same_layout()
    {
        var condition = Compiling.Condition("close > open and rsi(3) < 90", M5);
        var layout = new FrameOptions { HistoryDepth = condition.HistoryDepth };
        var first = Frames.Of(layout, [.. condition.Features]);
        var second = Frames.Of(layout, [.. condition.Features]);
        var boundFirst = condition.Bind(first);
        var boundSecond = condition.Bind(second);
        var sameObject = new List<Truth>();
        var ownObject = new List<Truth>();

        foreach (var bar in Bars)
        {
            first.Step(bar);
            second.Step(bar);

            // the condition bound to the first frame, evaluated against the second frame of the same layout, says what the one bound to the second frame says
            sameObject.Add(boundFirst.EvaluateTruth(second));
            ownObject.Add(boundSecond.EvaluateTruth(second));
        }

        sameObject.ShouldBe(ownObject);
        sameObject.Distinct().Count().ShouldBeGreaterThan(1, "the series makes the condition change");
    }

    [Fact]
    public void Two_binds_give_two_independent_conditions_with_the_same_answers_and_the_condition_they_came_from()
    {
        var condition = Compiling.Condition("rising(close, 2)", M5);
        var frame = Frames.Of(new FrameOptions { HistoryDepth = 3 }, [.. condition.Features]);
        var a = condition.Bind(frame);
        var b = condition.Bind(frame);

        a.ShouldNotBeSameAs(b);
        a.Condition.ShouldBeSameAs(condition);
        foreach (var bar in Bars)
        {
            frame.Step(bar);
            a.EvaluateTruth(frame).ShouldBe(b.EvaluateTruth(frame));
            a.Evaluate(frame).ShouldBe(a.EvaluateTruth(frame) == Truth.True);
        }
    }

    [Fact]
    public void Binding_again_after_the_frame_was_reset_works_and_the_frame_replays_the_same_answers()
    {
        var condition = Compiling.Condition("close > open", M5);
        var frame = Frames.Of([.. condition.Features]);
        var bound = condition.Bind(frame);
        var first = Bars.Select(bar => { frame.Step(bar); return bound.EvaluateTruth(frame); }).ToList();

        frame.Reset();
        var second = Bars.Select(bar => { frame.Step(bar); return bound.EvaluateTruth(frame); }).ToList();

        second.ShouldBe(first);
    }

    [Fact]
    public void Binding_the_same_new_condition_on_many_threads_at_once_compiles_it_safely_and_all_get_the_same_answers()
    {
        var bars = Series.Sessions(11, 2);
        const int Threads = 16;
        for (var round = 0; round < 20; round++)
        {
            var condition = Compiling.Condition("crosses_above(close, ema(5)) or rising(close, 3) and rsi(3) < 80", M5);
            var results = new string[Threads];
            using var gate = new Barrier(Threads);

            Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, t =>
            {
                var frame = Frames.Of(new FrameOptions { HistoryDepth = condition.HistoryDepth }, [.. condition.Features]);
                gate.SignalAndWait();
                var bound = condition.Bind(frame);
                var word = new char[bars.Length];
                for (var i = 0; i < bars.Length; i++)
                {
                    frame.Step(bars[i]);
                    word[i] = bound.EvaluateTruth(frame) switch { Truth.True => 'T', Truth.False => 'F', _ => 'U' };
                }

                results[t] = new string(word);
            });

            results.Distinct().Count().ShouldBe(1, "round " + round);
            results[0].ShouldContain("T", Case.Sensitive);
        }
    }

    [Fact]
    public void The_features_of_a_condition_are_read_only_and_the_bind_does_not_change_the_condition()
    {
        var condition = Compiling.Condition("close > open", M5);
        var features = condition.Features.ToList();

        condition.Bind(Frames.Of([.. features]));
        condition.Bind(Frames.Of([.. features]));

        condition.Features.ShouldBe(features);
        ((IList<FeatureSpec>)condition.Features).IsReadOnly.ShouldBeTrue();
    }
}
