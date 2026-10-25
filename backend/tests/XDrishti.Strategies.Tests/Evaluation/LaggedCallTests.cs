using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Syntax;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>P3-02f Part B: <c>f(args)[k]</c> is the feature <c>k</c> closed bars of its own timeframe ago, worked out by hand on a ramp and a step.</summary>
public sealed class LaggedCallTests
{
    private static readonly DateOnly Mon = Clock.Mon;

    /// <summary>close = 1 … 30, high = close + 0.5, low = close - 0.5.</summary>
    private static Bar[] Ramp() => [.. Enumerable.Range(1, 30).Select(i => Clock.Bar(Mon, Clock.Open + i - 1, i, i + 0.5, i - 0.5, i))];

    [Fact]
    public void A_lagged_highest_is_the_highest_of_the_bar_before_and_is_not_ready_until_the_window_and_the_lag_are_there()
    {
        // bar i: highest(5) = i + 0.5 once 5 bars exist, so highest(5)[1] = i - 0.5 = close - 0.5 from bar 6 (5 bars + 1)
        var rig = Rig.Of(Timeframe.M1, "highest(5)[1] == close - 0.5");

        rig.Run(Ramp().Take(10)).ShouldBe("UUUUUTTTTT");
    }

    [Fact]
    public void A_lag_of_two_on_lowest_reads_the_window_two_bars_back()
    {
        // lowest(5, source=low)[2] at bar i = the lowest low of bars i-6 … i-2 = i - 6.5; ready from bar 7
        var rig = Rig.Of(Timeframe.M1, "lowest(5, source=low)[2] == close - 6.5");

        rig.Run(Ramp().Take(10)).ShouldBe("UUUUUUTTTT");
    }

    [Fact]
    public void Highest_with_the_lag_of_one_is_one_step_below_the_unlagged_value_on_a_ramp()
    {
        // bar 10: highest(5) = 10.5 and highest(5)[1] = 9.5
        var rig = Rig.Of(Timeframe.M1, ["highest(5) == 10.5 and highest(5)[1] == 9.5", "highest(5)[1] >= highest(5)"]);
        var truths = Ramp().Take(10).Select(b => rig.Step(b)).ToList();

        truths[9][0].ShouldBe(Truth.True);
        truths[9][1].ShouldBe(Truth.False);
    }

    [Fact]
    public void A_crossing_of_a_lagged_call_is_true_on_the_first_bar_above_the_previous_high_only()
    {
        // closes (= highs) 10 10 10 10 10 12 13 13: highest(3)[1] is the high of the three bars before. Bar 6: 12 > 10 and the previous close 10 <= 10: true. Bar 7: 13 > 12, but the previous close 12 > 10: false.
        var rig = Rig.Of(Timeframe.M1, "crosses_above(close, highest(3)[1])");

        rig.Run(Clock.Closes(Mon, 10, 10, 10, 10, 10, 12, 13, 13)).ShouldBe("UUUUFTFF");
    }

    [Theory]
    [InlineData("rsi(14) > 50")]
    [InlineData("atr(14) > 0.5")]
    [InlineData("nr(3) > 0")]
    [InlineData("ema(5) > 200")]
    public void A_lagged_rolling_call_is_the_same_condition_one_bar_later(string plain)
    {
        var bars = Series.Sessions(7, 1).Take(80).ToArray();
        var lagged = plain.Replace(" >", "[1] >", StringComparison.Ordinal);

        var now = Rig.Of(Timeframe.M1, plain).Run(bars);
        var later = Rig.Of(Timeframe.M1, lagged).Run(bars);

        later[0].ShouldBe('U');
        later[1..].ShouldBe(now[..^1]);
    }

    [Fact]
    public void A_lagged_hourly_ema_is_known_from_the_21st_hourly_close_and_holds_the_value_of_the_20th()
    {
        // a session of 375 minutes holds 7 hourly bars (six of 60 minutes and one of 15), so the 20th hourly close is the last minute of the 6th bar of the 3rd day
        // and the 21st is the last minute of that day; ema(20) is ready at the 20th close and its lag of one at the 21st
        var bars = new List<Bar>();
        var close = 100.0;
        foreach (var day in new[] { Clock.Mon, Clock.Tue, Clock.Wed, Clock.Thu })
        {
            for (var m = 0; m < 375; m++)
            {
                close += (m % 7) - 3 + 0.25;
                bars.Add(Clock.Close(day, Clock.Open + m, close));
            }
        }

        var plain = Rig.Of(Timeframe.M1, "tf(\"1h\").ema(20) > 0");
        var lagged = Rig.Of(Timeframe.M1, "tf(\"1h\").ema(20)[1] > 0");
        var spec = Specs.Of("ema", Timeframe.H1, 20);
        var valueAtTwentieth = double.NaN;
        var firstPlain = -1;
        var firstLagged = -1;
        for (var i = 0; i < bars.Count; i++)
        {
            if (plain.Step(bars[i])[0] == Truth.True && firstPlain < 0)
            {
                firstPlain = i;
                valueAtTwentieth = plain.Frame.At(plain.Frame.HandleOf(spec), 0);
            }

            if (lagged.Step(bars[i])[0] == Truth.True && firstLagged < 0)
            {
                firstLagged = i;
                lagged.Frame.At(lagged.Frame.HandleOf(spec), 1).ShouldBe(valueAtTwentieth);
            }
        }

        firstPlain.ShouldBe(2 * 375 + 359);
        firstLagged.ShouldBe(2 * 375 + 374);
    }

    [Theory]
    [InlineData("highest(20)[1]")]
    [InlineData("lowest(5)[2]")]
    [InlineData("rsi(14)[1]")]
    [InlineData("atr(14)[3]")]
    [InlineData("tf(\"1h\").ema(20)[1]")]
    [InlineData("nr(7)[1]")]
    public void The_pinned_lagged_calls_compile(string call)
    {
        Compiling.Of(call + " > 0", Timeframe.M5).IsSuccess.ShouldBeTrue(call);
    }

    [Theory]
    [InlineData("highest(20)[0] > 1", LintCodes.NegativeOrZeroLag)]
    [InlineData("highest(20)[-1] > 1", LintCodes.NegativeOrZeroLag)]
    [InlineData("abs(close)[1] > 1", LintCodes.LagOnNonFeature)]
    [InlineData("highest(20)[256] > 1", LintCodes.LagTooDeep)]
    [InlineData("crosses_above(close, highest(20)[255])", LintCodes.LagTooDeep)]
    public void An_unusable_lag_on_a_call_has_the_code_of_a_lag_on_a_reference(string source, string code)
    {
        Compiling.OnlyError(source).Code.ShouldBe(code);
    }

    [Theory]
    [InlineData("highest(20)[1.5] > 1", SyntaxCodes.BadSubscript)]
    [InlineData("(close + open)[1] > 1", SyntaxCodes.SubscriptOnNonReference)]
    [InlineData("highest(20)[1][2] > 1", SyntaxCodes.SubscriptOnNonReference)]
    [InlineData("1[1] > 1", SyntaxCodes.SubscriptOnNonReference)]
    public void The_shapes_that_stay_refused_keep_their_syntax_code(string source, string code)
    {
        Compiling.Failures(source)[0].Code.ShouldBe(code);
    }

    [Fact]
    public void A_lagged_call_reads_one_bar_more_history_per_lag()
    {
        var rig = Rig.Of(Timeframe.M1, "highest(20)[3] > 0");

        rig.Plan.HistoryDepth.ShouldBe(4);
    }

    [Fact]
    public void A_lagged_per_session_call_is_not_ready_on_the_first_bar_of_a_session()
    {
        var bars = new[] { Mon, Clock.Tue }.SelectMany(d => Enumerable.Range(0, 4).Select(i => Clock.Bar(d, Clock.Open + i, 10, 11, 9, 10 + i))).ToArray();
        var rig = Rig.Of(Timeframe.M1, "or_high(2)[1] > 0");

        rig.Run(bars)[4].ShouldBe('U');
    }
}
