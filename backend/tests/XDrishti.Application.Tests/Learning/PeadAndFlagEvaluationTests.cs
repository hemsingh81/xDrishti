using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning.Extensions;
using static XDrishti.Application.Tests.Learning.EvaluationKit;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Protocols P and L through the use case (P15-10): today's honest NOT_EVALUABLE, the ledger of the four variants before the verdict, the hold-out once, and the flag that is read and never recomputed.</summary>
public sealed class PeadAndFlagEvaluationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PeadEvaluation Pead(Rig rig, IReadOnlyList<PeadEvent> events) => new(new EvaluationKit.Pead(events), rig.Writer, rig.Store, NullLogger<PeadEvaluation>.Instance);

    private static FlagEvaluation Flag(Rig rig, FlagGrade? grade) => new(new EvaluationKit.Flag(grade), rig.Writer, NullLogger<FlagEvaluation>.Instance);

    // The kit's planted events, with the event dates shifted to a calendar the protocol can season: the kit already uses quarter starts.
    private static List<PeadEvent> Planted() => PeadEventsKit.Events(20, 0.5);

    [Fact]
    public async Task Today_with_no_pead_events_the_answer_is_not_evaluable_with_the_seasons_named_and_no_trial_is_counted()
    {
        var rig = NewRig();

        var outcome = (await Pead(rig, []).RunAsync(false, Ct)).Value;

        outcome.Records.Single().Verdict.ShouldBe("NOT_EVALUABLE");
        outcome.Records.Single().Missing.ShouldBe(["seasons 0 of 20"]);
        rig.Ledger.Rows.ShouldBeEmpty();
        rig.Store.Records.Single().Protocol.ShouldBe("P");
    }

    [Fact]
    public async Task A_planted_effect_counts_the_four_variants_before_the_verdict_freezes_the_choice_and_is_a_keep()
    {
        var rig = NewRig();

        var outcome = (await Pead(rig, Planted()).RunAsync(false, Ct)).Value;

        outcome.Records.Single().Verdict.ShouldBe("KEEP");
        rig.Log.Take(4).ShouldBe(["ledger.append", "ledger.append", "ledger.append", "ledger.append"]);
        rig.Ledger.Rows.Select(r => r.StrategyHash).Distinct().Count().ShouldBe(4);
        rig.Store.Records.Single().Trials.ShouldBe(4);
        rig.Store.Records.Single().InputsJson.ShouldContain("\"frozen_variant\":\"sue_yoy/5\"");
        rig.Store.Records.Single().TrialHash.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_hold_out_is_read_once_with_the_flag_after_a_keep_then_refused()
    {
        var rig = NewRig();
        var evaluation = Pead(rig, Planted());

        await evaluation.RunAsync(false, Ct);
        rig.Store.Records.ShouldAllBe(r => !r.HoldOutRead);
        var read = (await evaluation.RunAsync(true, Ct)).Value;
        var again = await evaluation.RunAsync(true, Ct);

        read.Records.Select(r => r.HoldOutRead).ShouldBe([false, true]);
        read.Records[1].Verdict.ShouldBe("KEEP");
        again.Error.Code.ShouldBe("extensions.holdout_used");
    }

    [Fact]
    public async Task Without_a_grade_the_flag_and_its_extraction_are_not_evaluable_and_the_second_names_protocol_l()
    {
        var rig = NewRig();

        var outcome = (await Flag(rig, null).RunAsync(Ct)).Value;

        outcome.Records.Select(r => (r.Protocol, r.Verdict)).ShouldBe([("L", "NOT_EVALUABLE"), ("X", "NOT_EVALUABLE")]);
        outcome.Records[0].Missing.ShouldBe(["no accepted eval run with set D"]);
        outcome.Records[1].Missing.Single().ShouldStartWith("protocol_L_not_passed");
        rig.Ledger.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_passed_grade_is_a_keep_that_links_the_eval_run_and_the_extraction_waits_for_its_rows()
    {
        var rig = NewRig();

        var outcome = (await Flag(rig, new FlagGrade("run-9", "fp-9", 240, true, true, "m", "p")).RunAsync(Ct)).Value;

        outcome.Records[0].Verdict.ShouldBe("KEEP");
        rig.Store.Records[0].InputsJson.ShouldContain("\"eval_run_id\":\"run-9\"");
        outcome.Records[1].Missing.Single().ShouldContain("not built");
    }

    [Fact]
    public async Task A_failed_gating_grade_is_a_drop()
    {
        var outcome = (await Flag(NewRig(), new FlagGrade("run-9", "fp-9", 240, true, false, null, null)).RunAsync(Ct)).Value;

        outcome.Records[0].Verdict.ShouldBe("DROP");
        outcome.Records[1].Missing.Single().ShouldStartWith("protocol_L_not_passed");
    }
}
