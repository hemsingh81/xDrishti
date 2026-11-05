using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10c-2 (ADR 0217), spec AC9: the truth table of the influence predicate. Each term false in turn gives false with its reason, all true gives true, any unavailable figure gives false, a spent hold-out is refused whatever the numbers.</summary>
public sealed class MlInfluenceTests
{
    private static readonly MlInfluenceOptions Options = new();

    private static MlHoldOutFacts GoodHoldOut(bool reused = false, bool kept = true, int? rows = 150, double? p = 0.97, double? effect = 0.05) => new(kept, reused, rows, p, effect);

    private static MlForwardFacts GoodForward(int? n = 40, double? weeks = 6, double? spread = 0.1, double? ece = 3) => new(n, weeks, spread, ece);

    private static MlInfluenceFacts AllTrue() => new(GoodHoldOut(), GoodForward(), false, true);

    private static MlTermResult Term(MlInfluenceResult r, string name) => r.Terms.Single(t => t.Name == name);

    [Fact]
    public void All_four_terms_true_gives_true_with_no_reason()
    {
        var r = MlInfluence.Evaluate(AllTrue(), Options);

        r.MayInfluence.ShouldBeTrue();
        r.Reasons.ShouldBeEmpty();
        r.Terms.Select(t => t.Name).ShouldBe(MlInfluence.TermNames);
        r.Terms.ShouldAllBe(t => t.State == MlTermState.Holds);
    }

    [Fact]
    public void The_default_is_false_everything_unavailable_and_the_switch_off()
    {
        var r = MlInfluence.Evaluate(new MlInfluenceFacts(null, null, null, false), Options);

        r.MayInfluence.ShouldBeFalse();
        Term(r, MlInfluence.HoldOutBeatsNoMl).State.ShouldBe(MlTermState.Unavailable);
        Term(r, MlInfluence.ForwardShadowOk).State.ShouldBe(MlTermState.Unavailable);
        Term(r, MlInfluence.NoCalibrationAlarm).State.ShouldBe(MlTermState.Unavailable);
        Term(r, MlInfluence.SwitchOn).State.ShouldBe(MlTermState.Fails);
        r.Reasons.Select(x => x.Code).ShouldBe(["holdout_not_scored", "forward_not_available", "calibration_alarm_unavailable", "switch_off"]);
    }

    public static TheoryData<string, MlInfluenceFacts, string, MlTermState, string> OneTermFalse() => new()
    {
        { "hold-out reused", AllTrue() with { HoldOut = GoodHoldOut(reused: true) }, MlInfluence.HoldOutBeatsNoMl, MlTermState.Fails, "holdout_reused" },
        { "no hold-out kept", AllTrue() with { HoldOut = GoodHoldOut(kept: false) }, MlInfluence.HoldOutBeatsNoMl, MlTermState.Fails, "holdout_not_kept" },
        { "99 hold-out rows", AllTrue() with { HoldOut = GoodHoldOut(rows: 99) }, MlInfluence.HoldOutBeatsNoMl, MlTermState.Fails, "holdout_too_few_rows" },
        { "probability 0.9499", AllTrue() with { HoldOut = GoodHoldOut(p: 0.9499) }, MlInfluence.HoldOutBeatsNoMl, MlTermState.Fails, "holdout_not_beating_baseline" },
        { "negative policy effect", AllTrue() with { HoldOut = GoodHoldOut(effect: -0.01) }, MlInfluence.HoldOutBeatsNoMl, MlTermState.Fails, "holdout_policy_effect_negative" },
        { "29 graded candidates", AllTrue() with { Forward = GoodForward(n: 29) }, MlInfluence.ForwardShadowOk, MlTermState.Fails, "forward_too_few_candidates" },
        { "3 weeks", AllTrue() with { Forward = GoodForward(weeks: 3) }, MlInfluence.ForwardShadowOk, MlTermState.Fails, "forward_too_short" },
        { "negative spread", AllTrue() with { Forward = GoodForward(spread: -0.001) }, MlInfluence.ForwardShadowOk, MlTermState.Fails, "forward_spread_negative" },
        { "rolling ECE 5.01 pp", AllTrue() with { Forward = GoodForward(ece: 5.01) }, MlInfluence.ForwardShadowOk, MlTermState.Fails, "forward_ece_too_high" },
        { "calibration alarm", AllTrue() with { CalibrationAlarm = true }, MlInfluence.NoCalibrationAlarm, MlTermState.Fails, "calibration_alarm" },
        { "switch off", AllTrue() with { SwitchOn = false }, MlInfluence.SwitchOn, MlTermState.Fails, "switch_off" },
    };

    [Theory]
    [MemberData(nameof(OneTermFalse))]
    public void Each_term_false_in_turn_gives_false_with_its_reason(string why, MlInfluenceFacts facts, string term, MlTermState state, string code)
    {
        var r = MlInfluence.Evaluate(facts, Options);

        r.MayInfluence.ShouldBeFalse(why);
        Term(r, term).State.ShouldBe(state);
        Term(r, term).Reasons.ShouldContain(x => x.Code == code);
        r.Reasons.Select(x => x.Code).ShouldBe([code], why + ": only that term fails");
    }

    [Fact]
    public void The_bars_are_inclusive()
    {
        var edge = new MlInfluenceFacts(GoodHoldOut(rows: 100, p: 0.95, effect: 0), GoodForward(n: 30, weeks: 4, spread: 0, ece: 5), false, true);

        MlInfluence.Evaluate(edge, Options).MayInfluence.ShouldBeTrue();
    }

    public static TheoryData<string, MlInfluenceFacts, string, string> OneFigureUnavailable() => new()
    {
        { "hold-out not scored", AllTrue() with { HoldOut = null }, MlInfluence.HoldOutBeatsNoMl, "holdout_not_scored" },
        { "hold-out rows", AllTrue() with { HoldOut = GoodHoldOut(rows: null) }, MlInfluence.HoldOutBeatsNoMl, "holdout_rows_unavailable" },
        { "hold-out probability", AllTrue() with { HoldOut = GoodHoldOut(p: null) }, MlInfluence.HoldOutBeatsNoMl, "holdout_probability_unavailable" },
        { "hold-out probability NaN", AllTrue() with { HoldOut = GoodHoldOut(p: double.NaN) }, MlInfluence.HoldOutBeatsNoMl, "holdout_probability_unavailable" },
        { "hold-out policy effect", AllTrue() with { HoldOut = GoodHoldOut(effect: null) }, MlInfluence.HoldOutBeatsNoMl, "holdout_policy_effect_unavailable" },
        { "hold-out policy effect infinite", AllTrue() with { HoldOut = GoodHoldOut(effect: double.PositiveInfinity) }, MlInfluence.HoldOutBeatsNoMl, "holdout_policy_effect_unavailable" },
        { "forward not available", AllTrue() with { Forward = null }, MlInfluence.ForwardShadowOk, "forward_not_available" },
        { "forward candidates", AllTrue() with { Forward = GoodForward(n: null) }, MlInfluence.ForwardShadowOk, "forward_candidates_unavailable" },
        { "forward weeks", AllTrue() with { Forward = GoodForward(weeks: null) }, MlInfluence.ForwardShadowOk, "forward_weeks_unavailable" },
        { "forward spread", AllTrue() with { Forward = GoodForward(spread: double.NaN) }, MlInfluence.ForwardShadowOk, "forward_spread_unavailable" },
        { "forward ECE", AllTrue() with { Forward = GoodForward(ece: null) }, MlInfluence.ForwardShadowOk, "forward_ece_unavailable" },
        { "calibration detector unread", AllTrue() with { CalibrationAlarm = null }, MlInfluence.NoCalibrationAlarm, "calibration_alarm_unavailable" },
    };

    [Theory]
    [MemberData(nameof(OneFigureUnavailable))]
    public void An_unavailable_figure_gives_false_and_never_counts_as_a_pass(string why, MlInfluenceFacts facts, string term, string code)
    {
        var r = MlInfluence.Evaluate(facts, Options);

        r.MayInfluence.ShouldBeFalse(why);
        Term(r, term).State.ShouldBe(MlTermState.Unavailable);
        Term(r, term).Holds.ShouldBeFalse();
        r.Reasons.Select(x => x.Code).ShouldBe([code]);
    }

    [Fact]
    public void A_spent_hold_out_is_refused_whatever_the_numbers()
    {
        var spent = AllTrue() with { HoldOut = GoodHoldOut(reused: true, rows: 10000, p: 0.9999, effect: 5) };

        var r = MlInfluence.Evaluate(spent, Options);

        r.MayInfluence.ShouldBeFalse();
        Term(r, MlInfluence.HoldOutBeatsNoMl).Reasons.Select(x => x.Code).ShouldBe(["holdout_reused"]);
    }

    [Fact]
    public void A_spent_hold_out_is_refused_even_with_no_figures()
    {
        var r = MlInfluence.Evaluate(AllTrue() with { HoldOut = new MlHoldOutFacts(true, true, null, null, null) }, Options);

        Term(r, MlInfluence.HoldOutBeatsNoMl).State.ShouldBe(MlTermState.Fails);
        Term(r, MlInfluence.HoldOutBeatsNoMl).Reasons.Select(x => x.Code).ShouldBe(["holdout_reused"]);
    }

    [Fact]
    public void A_failure_outranks_an_unavailable_figure_in_the_same_term_and_lists_both_reasons()
    {
        var r = MlInfluence.Evaluate(AllTrue() with { HoldOut = GoodHoldOut(rows: 50, p: null) }, Options);

        var term = Term(r, MlInfluence.HoldOutBeatsNoMl);
        term.State.ShouldBe(MlTermState.Fails);
        term.Reasons.Select(x => x.Code).ShouldBe(["holdout_too_few_rows", "holdout_probability_unavailable"]);
    }

    [Fact]
    public void Several_terms_false_list_every_reason_in_term_order()
    {
        var r = MlInfluence.Evaluate(new MlInfluenceFacts(GoodHoldOut(reused: true), GoodForward(n: 1), true, false), Options);

        r.MayInfluence.ShouldBeFalse();
        r.Reasons.Select(x => x.Code).ShouldBe(["holdout_reused", "forward_too_few_candidates", "calibration_alarm", "switch_off"]);
        r.Reasons.ShouldAllBe(x => x.Text.Length > 10);
    }

    [Fact]
    public void A_result_without_all_four_terms_is_never_true()
    {
        new MlInfluenceResult([]).MayInfluence.ShouldBeFalse();
        new MlInfluenceResult([new MlTermResult(MlInfluence.SwitchOn, MlTermState.Holds, [])]).MayInfluence.ShouldBeFalse();
    }

    [Fact]
    public void A_result_whose_terms_are_not_the_four_in_order_is_never_true_even_if_all_hold()
    {
        MlTermResult Holds(string name) => new(name, MlTermState.Holds, []);

        new MlInfluenceResult([.. MlInfluence.TermNames.Select(Holds)]).MayInfluence.ShouldBeTrue();
        new MlInfluenceResult([.. MlInfluence.TermNames.Reverse().Select(Holds)]).MayInfluence.ShouldBeFalse();
        new MlInfluenceResult([Holds("A"), Holds("B"), Holds("C"), Holds("D")]).MayInfluence.ShouldBeFalse();
        new MlInfluenceResult([.. MlInfluence.TermNames.Select(Holds), Holds("IsChampion")]).MayInfluence.ShouldBeFalse();
    }

    [Fact]
    public void An_unset_state_is_unavailable_which_never_holds()
    {
        default(MlTermState).ShouldBe(MlTermState.Unavailable);
        new MlTermResult("x", default, []).Holds.ShouldBeFalse();
    }

    [Fact]
    public void The_thresholds_come_from_the_options()
    {
        var strict = new MlInfluenceOptions(HoldOutMinRows: 200);

        MlInfluence.Evaluate(AllTrue(), strict).Reasons.ShouldContain(x => x.Code == "holdout_too_few_rows");
    }

    [Fact]
    public void The_same_facts_give_the_same_answer()
    {
        var a = MlInfluence.Evaluate(AllTrue() with { SwitchOn = false }, Options);
        var b = MlInfluence.Evaluate(AllTrue() with { SwitchOn = false }, Options);

        b.Reasons.ShouldBe(a.Reasons);
        b.Terms.Select(t => t.State).ShouldBe(a.Terms.Select(t => t.State));
    }
}
