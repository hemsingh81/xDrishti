using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The two examples of criterion 8 with their exact text, <c>not (rsi(14) &gt; 55)</c> and <c>rsi(14) &lt; 45 or close &gt; 0</c>, over the warm-up of <c>rsi(14)</c> (ready after 15 bars of its timeframe): while it is not ready
/// the first can neither be true nor be turned into true by <c>not</c> and the second is true only because <c>close &gt; 0</c> is; once it is ready they are the ordinary conditions. What each must be is worked out from the
/// frame's own RSI value and readiness, which the tests read without the compiled code.
/// </summary>
public sealed class RsiWarmUpExamplesTests
{
    private static readonly string[] Sources =
    [
        "rsi(14) > 55",
        "not (rsi(14) > 55)",
        "rsi(14) < 45 or close > 0",
        "rsi(14) < 45 and close > 0",
        "not (rsi(14) < 45 or close < 0)",
        "not (rsi(14) > 55) and close > 0",
        "rsi(14) > 55 or not (close > 0)",
    ];

    private static Truth Of(bool holds) => holds ? Truth.True : Truth.False;

    // Kleene's definitions, written as plain rules (false decides an and, true decides an or, otherwise an unknown makes the result unknown)
    private static Truth And(Truth a, Truth b) => a == Truth.False || b == Truth.False ? Truth.False : a == Truth.Unknown || b == Truth.Unknown ? Truth.Unknown : Truth.True;

    private static Truth Or(Truth a, Truth b) => a == Truth.True || b == Truth.True ? Truth.True : a == Truth.Unknown || b == Truth.Unknown ? Truth.Unknown : Truth.False;

    private static Truth Not(Truth a) => a == Truth.Unknown ? Truth.Unknown : Of(a == Truth.False);

    /// <summary>
    /// What each source must be. The close of the strategy's timeframe is ready from the first closed bar of it (the first step on 1m, the fifth on 5m …), so <c>close &gt; 0</c> is itself unknown until then; <c>close &lt; 0</c>
    /// is false from then on.
    /// </summary>
    private static Truth[] Expected(bool rsiReady, double rsi, bool closeReady)
    {
        var above = rsiReady ? Of(rsi > 55) : Truth.Unknown;
        var below = rsiReady ? Of(rsi < 45) : Truth.Unknown;
        var closePositive = closeReady ? Truth.True : Truth.Unknown;
        var closeNegative = closeReady ? Truth.False : Truth.Unknown;
        return
        [
            above,
            Not(above),
            Or(below, closePositive),
            And(below, closePositive),
            Not(Or(below, closeNegative)),
            And(Not(above), closePositive),
            Or(above, Not(closePositive)),
        ];
    }

    [Theory]
    [InlineData(Timeframe.M1, 15)]
    [InlineData(Timeframe.M5, 75)]
    [InlineData(Timeframe.M15, 225)]
    public void None_of_the_conditions_is_true_by_accident_during_the_warm_up_of_rsi_14_and_all_follow_the_value_after_it(Timeframe timeframe, int firstReadyStep)
    {
        var rig = Rig.Of(timeframe, Sources);
        var handle = rig.Frame.HandleOf(Specs.Rsi(timeframe));
        var closeHandle = rig.Frame.HandleOf(Specs.Close(timeframe));
        handle.ShouldBeGreaterThanOrEqualTo(0);
        closeHandle.ShouldBeGreaterThanOrEqualTo(0);
        rig.Frame.WarmUp(handle).ShouldBe(15, "rsi(14) is ready after 15 bars of its timeframe");
        var unknownSteps = 0;
        var readySteps = 0;
        var sawAbove = false;
        var sawBelow = false;

        foreach (var bar in Series.Sessions(2, 2).Take(firstReadyStep + 300))
        {
            rig.Frame.Step(bar);
            var ready = rig.Frame.IsReady(handle);
            var rsi = rig.Frame[handle];
            var expected = Expected(ready, rsi, rig.Frame.IsReady(closeHandle));
            var truths = rig.Read();

            truths.ShouldBe(expected, $"step {rig.Frame.Steps}: rsi ready {ready}, {rsi}");
            for (var i = 0; i < Sources.Length; i++)
            {
                rig.Bound[i].Evaluate(rig.Frame).ShouldBe(expected[i] == Truth.True, Sources[i]);
            }

            if (ready)
            {
                readySteps++;
                sawAbove |= rsi > 55;
                sawBelow |= rsi < 45;
                (rig.Frame.Steps >= firstReadyStep).ShouldBeTrue("ready earlier than its warm-up");
            }
            else
            {
                unknownSteps++;
                truths[0].ShouldBe(Truth.Unknown);
                truths[1].ShouldBe(Truth.Unknown, "not of an unknown is unknown, not true");
                rig.Bound[1].Evaluate(rig.Frame).ShouldBeFalse();
                truths[2].ShouldBe(rig.Frame.IsReady(closeHandle) ? Truth.True : Truth.Unknown, "close > 0 decides the or when it is ready, and then only");
            }
        }

        unknownSteps.ShouldBe(firstReadyStep - 1, "the step that closes the 15th bar is the first with a value");
        readySteps.ShouldBe(300 + 1);
        (sawAbove && sawBelow).ShouldBeTrue("the series takes the RSI both above 55 and below 45, so both branches ran");
    }
}
