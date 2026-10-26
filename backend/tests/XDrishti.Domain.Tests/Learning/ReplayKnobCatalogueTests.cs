using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Spec P8-18 criterion 5 (ADR 0129): the knobs a replay from the screen may change, and every refusal, by hand.</summary>
public sealed class ReplayKnobCatalogueTests
{
    private static readonly ExitCandidate Rr2 = new("FIXED_RR", [new ExitParameterValue("rr", [2])]);

    private static readonly ExitCandidate Be = new("PARTIAL_BE", [new ExitParameterValue("book_pct", [50]), new ExitParameterValue("t1_rr", [1]), new ExitParameterValue("t2_rr", [2])]);

    private static readonly SlippageConfig Slip = SlippageConfig.Create(1, 0.05, 2).Value;

    private static Result<CheckedKnob> Check(string? kind, string? name, double value, ExitCandidate? exit = null) => ReplayKnobCatalogue.Check(kind, name, value, exit ?? Rr2, Slip);

    [Fact]
    public void The_offers_are_the_plain_exit_parameters_of_the_baseline_and_the_three_slippage_components_and_never_a_parameter()
    {
        var offers = ReplayKnobCatalogue.Offers(Be, Slip);

        offers.Select(o => (o.Kind, o.Name, o.Current)).ShouldBe(
        [
            (ExperimentKnobKind.Exit, "PARTIAL_BE.book_pct", 50),
            (ExperimentKnobKind.Exit, "PARTIAL_BE.t1_rr", 1),
            (ExperimentKnobKind.Exit, "PARTIAL_BE.t2_rr", 2),
            (ExperimentKnobKind.Slippage, "slippage.ticks", 1),
            (ExperimentKnobKind.Slippage, "slippage.bar_range_frac", 0.05),
            (ExperimentKnobKind.Slippage, "slippage.stop_exit_extra_ticks", 2),
        ]);
        offers.ShouldNotContain(o => o.Name.Contains("parameter", StringComparison.Ordinal));
        offers.Where(o => o.Kind == ExperimentKnobKind.Slippage).ShouldAllBe(o => o.Min == o.Current, "a cheaper value is never offered");
    }

    [Fact]
    public void A_baseline_with_no_readable_exit_offers_slippage_only_and_an_exit_policy_without_plain_numbers_offers_no_exit()
    {
        ReplayKnobCatalogue.Offers(null, Slip).ShouldAllBe(o => o.Kind == ExperimentKnobKind.Slippage);
        ReplayKnobCatalogue.Offers(new ExitCandidate("FIXED_RR", [new ExitParameterValue("rr", [1.5, 2])]), Slip).ShouldAllBe(o => o.Kind == ExperimentKnobKind.Slippage);
        ReplayKnobCatalogue.Offers(new ExitCandidate("PYRAMID", [new ExitParameterValue("max_adds", [2])]), Slip).ShouldAllBe(o => o.Kind == ExperimentKnobKind.Slippage);
    }

    [Fact]
    public void A_valid_exit_knob_is_checked_with_the_two_values_as_text()
    {
        var knob = Check("exit", "FIXED_RR.rr", 3);

        knob.IsSuccess.ShouldBeTrue();
        knob.Value.Knob.ShouldBe(new ExperimentKnob("FIXED_RR.rr", ExperimentKnobKind.Exit, "2", "3"));
        knob.Value.Current.ShouldBe(2);
        knob.Value.Candidate.ShouldBe(3);
    }

    [Fact]
    public void A_dearer_slippage_is_a_valid_knob_and_a_cheaper_or_equal_one_is_refused_with_the_reason_of_rule_3()
    {
        Check("slippage", "slippage.ticks", 2).IsSuccess.ShouldBeTrue();
        Check("slippage", "slippage.bar_range_frac", 0.1).Value.Knob.CandidateValue.ShouldBe("0.1");

        var cheaper = Check("slippage", "slippage.ticks", 0.5);
        var same = Check("slippage", "slippage.ticks", 1);

        cheaper.Error.Code.ShouldBe("experiments.knob_not_allowed");
        cheaper.Error.Message.ShouldContain("a cheaper cost assumption cannot win (doc 09 §12 rule 3)");
        same.Error.Code.ShouldBe("experiments.knob_not_allowed");
        same.Error.Message.ShouldContain("nothing is changed");
    }

    [Theory]
    [InlineData("parameter", "or_min", 10)]
    [InlineData("Parameter", "rr", 3)]
    [InlineData("learning", "half_life", 3)]
    [InlineData(null, "FIXED_RR.rr", 3)]
    [InlineData("exit", "FIXED_RR.nothing", 3)]
    [InlineData("exit", "ATR_TRAIL.mult", 3)]
    [InlineData("exit", "rr", 3)]
    [InlineData("exit", null, 3)]
    [InlineData("slippage", "ticks", 3)]
    [InlineData("slippage", "slippage.gap_fill", 3)]
    public void A_parameter_an_unknown_kind_or_name_and_a_name_of_another_policy_are_refused(string? kind, string? name, double value)
    {
        var result = Check(kind, name, value);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("experiments.knob_not_allowed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(2)]
    public void A_value_outside_the_range_not_finite_or_unchanged_is_refused_before_anything_is_queued(double value) =>
        Check("exit", "FIXED_RR.rr", value).Error.Code.ShouldBe("experiments.knob_not_allowed");

    [Fact]
    public void Slippage_above_the_limits_and_an_unchanged_equal_number_spelled_differently_are_refused()
    {
        Check("slippage", "slippage.ticks", 101).IsFailure.ShouldBeTrue();
        Check("slippage", "slippage.bar_range_frac", 1.5).IsFailure.ShouldBeTrue();
        Check("exit", "FIXED_RR.rr", 2.0).Error.Message.ShouldContain("nothing is changed");
    }

    [Fact]
    public void Partial_break_even_keeps_t2_above_t1_and_a_baseline_without_an_exit_refuses_every_exit_knob()
    {
        Check("exit", "PARTIAL_BE.t2_rr", 1, Be).Error.Message.ShouldContain("t2_rr must stay above t1_rr");
        Check("exit", "PARTIAL_BE.t1_rr", 2.5, Be).IsFailure.ShouldBeTrue();
        Check("exit", "PARTIAL_BE.t2_rr", 3, Be).IsSuccess.ShouldBeTrue();
        ReplayKnobCatalogue.Check("exit", "FIXED_RR.rr", 3, null, Slip).Error.Message.ShouldContain("cannot be read");
    }

    [Theory]
    [InlineData(ExperimentJobState.Queued, ExperimentJobState.Running, true)]
    [InlineData(ExperimentJobState.Queued, ExperimentJobState.Cancelled, true)]
    [InlineData(ExperimentJobState.Queued, ExperimentJobState.Succeeded, false)]
    [InlineData(ExperimentJobState.Running, ExperimentJobState.Succeeded, true)]
    [InlineData(ExperimentJobState.Running, ExperimentJobState.Failed, true)]
    [InlineData(ExperimentJobState.Running, ExperimentJobState.Cancelled, true)]
    [InlineData(ExperimentJobState.Running, ExperimentJobState.Interrupted, true)]
    [InlineData(ExperimentJobState.Running, ExperimentJobState.Queued, false)]
    [InlineData(ExperimentJobState.Succeeded, ExperimentJobState.Running, false)]
    [InlineData(ExperimentJobState.Failed, ExperimentJobState.Queued, false)]
    [InlineData(ExperimentJobState.Cancelled, ExperimentJobState.Running, false)]
    [InlineData(ExperimentJobState.Interrupted, ExperimentJobState.Running, false)]
    public void The_state_machine_has_the_legal_moves_only_and_a_final_state_is_final(ExperimentJobState from, ExperimentJobState to, bool legal)
    {
        ExperimentJobStates.CanMove(from, to).ShouldBe(legal);
        if (ExperimentJobStates.IsFinal(from))
        {
            Enum.GetValues<ExperimentJobState>().ShouldAllBe(s => !ExperimentJobStates.CanMove(from, s));
        }
    }

    [Fact]
    public void Every_state_has_a_word_that_parses_back_and_an_unknown_word_does_not()
    {
        foreach (var state in Enum.GetValues<ExperimentJobState>())
        {
            ExperimentJobStates.Parse(ExperimentJobStates.Code(state)).ShouldBe(state);
        }

        ExperimentJobStates.Parse("done").ShouldBeNull();
        ExperimentJobStates.IsActive(ExperimentJobState.Queued).ShouldBeTrue();
        ExperimentJobStates.IsActive(ExperimentJobState.Running).ShouldBeTrue();
    }

    [Theory]
    [InlineData(1, 0.026)]
    [InlineData(4, 0.032)]
    [InlineData(9, 0.038)]
    public void The_required_improvement_of_a_trial_is_the_hand_worked_figure_the_preview_prints(int trials, double expected) =>
        new ExperimentOptions { Trials = trials }.RequiredImprovementR.ShouldBe(expected, 1e-12);
}
