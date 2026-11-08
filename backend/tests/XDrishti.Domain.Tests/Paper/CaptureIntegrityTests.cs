using XDrishti.Domain.Paper;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>Criterion I on its own (CF-11 SC-S3, ADR 0224): the same function the whole phase check uses, so the scorecard and the check cannot differ. The boundaries: no capture run, one disagreement, a pair pending exactly 3 and 4 sessions.</summary>
public sealed class CaptureIntegrityTests
{
    private static PhaseSessionFact Session(int day, int runs = 1, int integrity = 0, bool pending = false) =>
        new(new DateOnly(2026, 11, 2).AddDays(day), false, 0, 0, null, runs, !pending && runs > 0, pending, integrity);

    [Fact]
    public void No_sessions_is_not_evaluable()
    {
        var c = PaperPhaseCheck.CaptureIntegrity([]);

        c.Id.ShouldBe("I");
        c.Verdict.ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void Sessions_without_a_capture_run_are_not_evaluable()
    {
        PaperPhaseCheck.CaptureIntegrity([Session(0, runs: 0), Session(1, runs: 0)]).Verdict.ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public void Capture_runs_with_no_disagreement_and_nothing_stuck_are_met()
    {
        PaperPhaseCheck.CaptureIntegrity([Session(0), Session(1), Session(2)]).Verdict.ShouldBe("MET");
    }

    [Fact]
    public void One_disagreement_is_not_met_and_names_the_session()
    {
        var c = PaperPhaseCheck.CaptureIntegrity([Session(0), Session(1, integrity: 1), Session(2)]);

        c.Verdict.ShouldBe("NOT MET");
        c.Label.ShouldContain("1 stored entries disagreed");
        c.Label.ShouldContain("2026-11-03");
    }

    [Fact]
    public void A_pair_pending_for_three_later_sessions_is_met_and_for_four_is_not_met()
    {
        PaperPhaseCheck.CaptureIntegrity([Session(0, pending: true), Session(1), Session(2), Session(3)]).Verdict.ShouldBe("MET");
        PaperPhaseCheck.CaptureIntegrity([Session(0, pending: true), Session(1), Session(2), Session(3), Session(4)]).Verdict.ShouldBe("NOT MET");
    }

    [Fact]
    public void The_whole_check_judges_criterion_I_with_this_same_function()
    {
        var epoch = new PhaseEpochFact(2, new DateOnly(2026, 11, 2), "owner_restart", "start", 0, 0, 0);
        foreach (var sessions in new List<PhaseSessionFact>[] { [], [Session(0), Session(1)], [Session(0), Session(1, integrity: 2)] })
        {
            var facts = new PaperPhaseFacts(epoch, [epoch], new DateOnly(2026, 11, 6), true, sessions, [], null, [], null, "x", [], [], []);

            var whole = PaperPhaseCheck.Evaluate(facts).Criteria.Single(c => c.Id == "I");
            var alone = PaperPhaseCheck.CaptureIntegrity(sessions);

            alone.Verdict.ShouldBe(whole.Verdict);
            alone.Label.ShouldBe(whole.Label);
        }
    }
}
