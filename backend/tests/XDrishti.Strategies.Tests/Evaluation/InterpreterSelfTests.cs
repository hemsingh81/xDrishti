using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Tests.Support;
using XDrishti.Strategies.Tests.Support.Oracle;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>
/// The oracle is only worth something if it is right, so it is checked against answers calculated by hand from the spec's definitions, on frames of a few bars, before it is trusted to judge the compiler: Kleene's tables,
/// the crossings with equality at either bar, runs, <c>between</c> at both ends, a lag offset inside a crossing, <c>time</c>, a not-ready operand, <c>x / 0</c> and infinities, the weekday constants and the regime names.
/// </summary>
public sealed class InterpreterSelfTests
{
    private static readonly Leaf Close = Read("close", "close", true, new FeatureSpec("close"));

    private static readonly Leaf Open = Read("open", "open", true, new FeatureSpec("open"));

    private static readonly Leaf Sma5 = Read("sma(5)", "sma(5)", false, new FeatureSpec("sma", Timeframe.M1, PriceSource.Default, 5));

    private static readonly Leaf Sma3 = Read("sma(3)", "sma(3)", false, new FeatureSpec("sma", Timeframe.M1, PriceSource.Default, 3));

    private static Leaf Read(string text, string qualified, bool reference, FeatureSpec spec, int lag = 0) => new(new Feature(text, qualified, reference, spec), Timeframe.M1, false, lag);

    private static Lit L(string text) => new(text);

    private static FeatureFrame FrameOf(params FeatureSpec[] specs) => Frames.Of(new FrameOptions { HistoryDepth = 8 }, specs);

    /// <summary>The answers of the model after each close (a bar with only a close, one per minute from 09:15).</summary>
    private static string Run(Node model, params double[] closes)
    {
        var frame = FrameOf(new FeatureSpec("close"), new FeatureSpec("open"), Sma3.Spec, Sma5.Spec);
        var interpreter = new Interpreter(frame);
        return string.Concat(Clock.Closes(Clock.Mon, closes).Select(bar =>
        {
            frame.Step(bar);
            return Letter(interpreter.Evaluate(model));
        }));
    }

    private static char Letter(Tri value) => value switch { Tri.True => 'T', Tri.False => 'F', _ => 'U' };

    // ---- Kleene -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static readonly Node[] Atoms = [new BoolLit(false), new Cmp(">", Sma5, L("0")), new BoolLit(true)];

    private static string Table(Func<Node, Node, Node> combine)
    {
        var frame = FrameOf(Sma5.Spec);
        frame.Step(Clock.Close(Clock.Mon, Clock.Open, 10));
        frame.Step(Clock.Close(Clock.Mon, Clock.Open + 1, 10));
        var interpreter = new Interpreter(frame);
        return string.Join(",", Atoms.Select(a => string.Concat(Atoms.Select(b => Letter(interpreter.Evaluate(combine(a, b)))))));
    }

    [Fact]
    public void The_atoms_are_false_unknown_and_true()
    {
        var frame = FrameOf(Sma5.Spec);
        frame.Step(Clock.Close(Clock.Mon, Clock.Open, 10));
        frame.Step(Clock.Close(Clock.Mon, Clock.Open + 1, 10));
        var interpreter = new Interpreter(frame);

        Atoms.Select(a => Letter(interpreter.Evaluate(a))).ShouldBe(['F', 'U', 'T']);
    }

    [Fact]
    public void And_is_kleenes_table()
    {
        // rows: the left side false, unknown, true; columns: the right side false, unknown, true
        Table((a, b) => new Logic(true, a, b)).ShouldBe("FFF,FUU,FUT");
    }

    [Fact]
    public void Or_is_kleenes_table()
    {
        Table((a, b) => new Logic(false, a, b)).ShouldBe("FUT,UUT,TTT");
    }

    [Fact]
    public void Equal_and_not_equal_of_truth_values_are_unknown_when_either_side_is()
    {
        Table((a, b) => new Cmp("==", a, b)).ShouldBe("TUF,UUU,FUT");
        Table((a, b) => new Cmp("!=", a, b)).ShouldBe("FUT,UUU,TUF");
    }

    [Fact]
    public void Not_turns_false_into_true_and_true_into_false_and_leaves_unknown_alone()
    {
        Table((a, _) => new Not(a)).ShouldBe("TTT,UUU,FFF");
    }

    // ---- comparisons and the not-ready operand --------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_comparison_is_unknown_while_an_operand_is_not_ready_and_decided_after()
    {
        // sma(3) of 10, 11, 12, 13 is ready from the third bar: 11 and 12; against 11.5 the answers are unknown, unknown, false, true
        Run(new Cmp(">", Sma3, L("11.5")), 10, 11, 12, 13).ShouldBe("UUFT");
        Run(new Cmp("<=", Sma3, L("11")), 10, 11, 12, 13).ShouldBe("UUTF");
    }

    [Fact]
    public void All_six_comparisons_treat_equal_numbers_as_the_spec_says()
    {
        Run(new Cmp("<", Close, L("5")), 4, 5, 6).ShouldBe("TFF");
        Run(new Cmp("<=", Close, L("5")), 4, 5, 6).ShouldBe("TTF");
        Run(new Cmp(">", Close, L("5")), 4, 5, 6).ShouldBe("FFT");
        Run(new Cmp(">=", Close, L("5")), 4, 5, 6).ShouldBe("FTT");
        Run(new Cmp("==", Close, L("5")), 4, 5, 6).ShouldBe("FTF");
        Run(new Cmp("!=", Close, L("5")), 4, 5, 6).ShouldBe("TFT");
    }

    [Fact]
    public void Division_by_zero_is_not_ready_and_never_decides_a_comparison()
    {
        var divided = new Arith('/', Close, L("0"));

        Run(new Cmp("==", divided, L("1")), 5, 6).ShouldBe("UU");
        Run(new Cmp("!=", divided, L("1")), 5, 6).ShouldBe("UU");
        Run(new Cmp("<", new Arith('/', Close, new Arith('-', Close, Close)), L("1")), 5, 6).ShouldBe("UU");
        Run(new Not(new Cmp("<", divided, L("1"))), 5, 6).ShouldBe("UU");
    }

    [Fact]
    public void An_overflow_is_a_number_and_infinity_minus_infinity_is_not_ready()
    {
        var infinity = new Arith('*', L("1e308"), L("10"));

        Run(new Cmp(">", infinity, L("1")), 5).ShouldBe("T");
        Run(new Cmp("==", infinity, infinity), 5).ShouldBe("T");
        Run(new Cmp("==", new Arith('-', infinity, infinity), L("0")), 5).ShouldBe("U");
    }

    [Fact]
    public void Min_max_and_abs_pass_a_not_ready_value_on()
    {
        Run(new Cmp(">", new MinMax(true, Sma3, L("0")), L("-1")), 10, 11, 12).ShouldBe("UUT");
        Run(new Cmp(">", new MinMax(false, Sma3, L("100")), L("-1")), 10, 11, 12).ShouldBe("UUT");
        Run(new Cmp(">=", new Abs(new Neg(Sma3)), L("0")), 10, 11, 12).ShouldBe("UUT");
        Run(new Cmp("==", new Abs(Close), L("5")), 5, -5, 4).ShouldBe("TTF");
    }

    // ---- lagged calls ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Crosses_above_needs_the_value_above_now_and_not_above_before_and_equality_before_counts_as_not_above()
    {
        // closes 5 7 7 9 6 6 8 3 against 7: bar 4 (9 > 7, before 7 <= 7) and bar 7 (8 > 7, before 6 <= 7); bar 2 and 3 equal 7 now, so no
        Run(new Crosses(true, Close, L("7")), 5, 7, 7, 9, 6, 6, 8, 3).ShouldBe("UFFTFFTF");
    }

    [Fact]
    public void Crosses_below_needs_the_value_below_now_and_not_below_before_and_equality_before_counts_as_not_below()
    {
        // bar 5 (6 < 7, before 9 >= 7), bar 8 (3 < 7, before 8 >= 7); bar 6 is below but was already below
        Run(new Crosses(false, Close, L("7")), 5, 7, 7, 9, 6, 6, 8, 3).ShouldBe("UFFFTFFT");
    }

    [Fact]
    public void A_subscript_inside_a_crossing_is_added_to_the_lag_of_the_crossing()
    {
        // close[1] against 7 reads the closes one bar back now (lag 1) and two bars back before (lag 2): unknown for two bars
        var lagged = Read("close[1]", "close", true, new FeatureSpec("close"), lag: 1);

        Run(new Crosses(true, lagged, L("7")), 5, 7, 7, 9, 6, 6, 8, 3).ShouldBe("UUFFTFFT");
    }

    [Fact]
    public void A_crossing_of_two_features_reads_both_of_them_one_bar_back()
    {
        // open is the close of the same bar here (a bar with only a close): close crossing above sma(3) with 10 10 10 12: the average is 10, 10.67; before: 10 <= 10
        Run(new Crosses(true, Close, Sma3), 10, 10, 10, 12).ShouldBe("UUUT");
    }

    [Fact]
    public void Rising_is_strict_at_every_step_and_unknown_until_n_plus_one_values_exist()
    {
        Run(new Streak(true, Close, 3), 1, 2, 3, 4, 5, 4, 6).ShouldBe("UUUTTFF");
    }

    [Fact]
    public void Falling_is_strict_at_every_step()
    {
        Run(new Streak(false, Close, 2), 5, 4, 3, 3, 1).ShouldBe("UUTFF");
    }

    [Fact]
    public void A_run_over_an_expression_reads_every_feature_in_it_at_each_lag()
    {
        // close + close is 2, 4, 6, 6, 8: rising over two steps holds at the third bar, not at the fourth (6 is not above 6) and not at the fifth (the step before it is the equal one)
        Run(new Streak(true, new Arith('+', Close, Close), 2), 1, 2, 3, 3, 4).ShouldBe("UUTFF");
    }

    [Fact]
    public void Between_includes_both_ends()
    {
        Run(new Between(Close, L("2"), L("4")), 1, 2, 3, 4, 5).ShouldBe("FTTTF");
    }

    [Fact]
    public void Between_is_unknown_when_any_of_the_three_is_not_ready()
    {
        // sma(3) is ready from the third bar: 2, 3, 4 at the bars 3, 4, 5 of the closes 1 2 3 4 5
        Run(new Between(Close, Sma3, L("4")), 1, 2, 3, 4, 5).ShouldBe("UUTTF");
        Run(new Between(Close, L("0"), Sma3), 1, 2, 3, 4, 5).ShouldBe("UUFFF");
        Run(new Between(Sma3, L("2"), L("4")), 1, 2, 3, 4, 5).ShouldBe("UUTTT");
    }

    // ---- time -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private static string TimeAnswers(string op, int minuteOfDay, params int[] barMinutes)
    {
        var frame = FrameOf(new FeatureSpec("close"));
        var interpreter = new Interpreter(frame);
        var model = new TimeCmp(op, minuteOfDay, "00:00", false);
        return string.Concat(barMinutes.Select(m =>
        {
            frame.Step(Clock.Close(Clock.Mon, m, 10));
            return Letter(interpreter.Evaluate(model));
        }));
    }

    [Fact]
    public void Time_is_the_close_of_the_last_bar_the_bar_09_44_makes_it_09_45()
    {
        var at = 9 * 60 + 45;

        TimeAnswers(">=", at, 9 * 60 + 43, 9 * 60 + 44, 9 * 60 + 45).ShouldBe("FTT");
        TimeAnswers("==", at, 9 * 60 + 43, 9 * 60 + 44, 9 * 60 + 45).ShouldBe("FTF");
        TimeAnswers(">", at, 9 * 60 + 43, 9 * 60 + 44, 9 * 60 + 45).ShouldBe("FFT");
        TimeAnswers("<", at, 9 * 60 + 43, 9 * 60 + 44, 9 * 60 + 45).ShouldBe("TFF");
        TimeAnswers("<=", at, 9 * 60 + 43, 9 * 60 + 44, 9 * 60 + 45).ShouldBe("TTF");
        TimeAnswers("!=", at, 9 * 60 + 43, 9 * 60 + 44, 9 * 60 + 45).ShouldBe("TFT");
    }

    [Fact]
    public void Time_is_unknown_before_the_frame_has_taken_a_bar()
    {
        var frame = FrameOf(new FeatureSpec("close"));

        new Interpreter(frame).Evaluate(new TimeCmp(">=", 555, "09:15", false)).ShouldBe(Tri.Unknown);
    }

    [Fact]
    public void The_last_bar_of_the_session_closes_at_15_30()
    {
        TimeAnswers("==", 15 * 60 + 30, 15 * 60 + 28, 15 * 60 + 29).ShouldBe("FT");
    }

    // ---- the calendar and the regime ----------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_weekday_constants_are_one_for_monday_to_seven_for_sunday()
    {
        var frame = FrameOf(new FeatureSpec("day_of_week"));
        var interpreter = new Interpreter(frame);
        var day = Read("day_of_week", "day_of_week", true, new FeatureSpec("day_of_week"));
        frame.Step(Clock.Close(Clock.Tue, Clock.Open, 10));

        interpreter.Evaluate(new Cmp("==", day, new Const("TUE", 2))).ShouldBe(Tri.True);
        interpreter.Evaluate(new Cmp("==", day, new Const("MON", 1))).ShouldBe(Tri.False);
        interpreter.Evaluate(new Cmp("<", new Const("MON", 1), day)).ShouldBe(Tri.True);
        interpreter.Evaluate(new Cmp(">", day, new Const("SUN", 7))).ShouldBe(Tri.False);
    }

    [Fact]
    public void The_regime_names_are_the_nine_of_doc_07_in_the_order_of_their_codes()
    {
        RegimeNames.All.ShouldBe(
        [
            "UP_LOW_VOL", "UP_NORMAL_VOL", "UP_HIGH_VOL",
            "RANGE_LOW_VOL", "RANGE_NORMAL_VOL", "RANGE_HIGH_VOL",
            "DOWN_LOW_VOL", "DOWN_NORMAL_VOL", "DOWN_HIGH_VOL",
        ]);
        RegimeNames.CodeOf("UP_LOW_VOL").ShouldBe(1);
        RegimeNames.CodeOf("RANGE_NORMAL_VOL").ShouldBe(5);
        RegimeNames.CodeOf("DOWN_HIGH_VOL").ShouldBe(9);
        for (var code = 1; code <= 9; code++)
        {
            Regime.Name(code).ShouldBe(RegimeNames.All[code - 1]);
        }
    }

    [Fact]
    public void A_regime_comparison_is_unknown_until_the_context_gives_a_regime_and_then_equal_only_to_its_own_code()
    {
        var bars = Series.Sparse(4, 60);
        var context = Contexts.Covering(DateOnly.FromDateTime(bars[0].Start.UtcDateTime), DateOnly.FromDateTime(bars[^1].Start.UtcDateTime), 4, daysBefore: 10);
        var frame = Frames.Of(new FrameOptions { HistoryDepth = 4, Context = context }, new FeatureSpec("regime"), new FeatureSpec("close"));
        var interpreter = new Interpreter(frame);
        var regime = Read("regime()", "regime()", false, new FeatureSpec("regime"));
        var sawUnknown = false;
        var sawReady = false;

        foreach (var bar in bars)
        {
            frame.Step(bar);
            var equalsOne = interpreter.Evaluate(new RegimeCmp(true, 1, "UP_LOW_VOL", false, regime));
            if (!frame.IsReady(0))
            {
                equalsOne.ShouldBe(Tri.Unknown);
                interpreter.Evaluate(new RegimeCmp(false, 1, null, false, regime)).ShouldBe(Tri.Unknown);
                sawUnknown = true;
                continue;
            }

            sawReady = true;
            var code = (int)frame[0];
            interpreter.Evaluate(new RegimeCmp(true, code, RegimeNames.All[code - 1], false, regime)).ShouldBe(Tri.True);
            interpreter.Evaluate(new RegimeCmp(false, code, RegimeNames.All[code - 1], true, regime)).ShouldBe(Tri.False);
            interpreter.Evaluate(new RegimeCmp(true, code % 9 + 1, null, false, regime)).ShouldBe(Tri.False);
            interpreter.Evaluate(new RegimeCmp(false, code % 9 + 1, null, false, regime)).ShouldBe(Tri.True);
        }

        sawUnknown.ShouldBeTrue("the regime was ready from the first bar");
        sawReady.ShouldBeTrue("the regime was never ready");
    }

    [Fact]
    public void The_facts_of_a_model_are_its_features_its_deepest_lag_plus_one_and_what_the_frame_needs()
    {
        var lagged = Read("close[3]", "close", true, new FeatureSpec("close"), lag: 3);
        var vix = new Leaf(new Feature("vix", "vix", true, new FeatureSpec("vix", Timeframe.M1), ReadsContext: true), Timeframe.M1, false, 0);

        var facts = ModelFacts.Of(new Logic(true, new Crosses(true, lagged, vix), new Streak(false, Open, 5)));

        facts.Features.Count.ShouldBe(3);
        facts.HistoryDepth.ShouldBe(6);
        facts.NeedsContext.ShouldBeTrue();
        facts.NeedsExpiryDays.ShouldBeFalse();
        facts.UsesTime.ShouldBeFalse();
        ModelFacts.Of(new TimeCmp(">", 600, "10:00", false)).UsesTime.ShouldBeTrue();
        ModelFacts.Of(new Cmp("<", L("1"), L("2"))).HistoryDepth.ShouldBe(1);
    }
}
