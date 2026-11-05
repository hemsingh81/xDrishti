using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Learning.MlNet.Tests;

/// <summary>The real model explains its predictions fold by fold, so the harness can say which direction each feature pushes and whether it holds (CF-10 part 10b, ADR 0215).</summary>
public sealed class MlNetComplexityTests
{
    [Fact]
    public void A_linear_signal_keeps_its_direction_in_every_fold_and_the_report_reads_the_model()
    {
        var rows = SyntheticRows.Make(1000, learnable: true);

        var report = WalkForwardValidator.Compare(rows, [new MlNetScorerFactory()], new ValidationOptions(Ladder: new LadderOptions())).Value;
        var complexity = report.Complexity.ShouldNotBeNull();
        var stability = complexity.Stability.Single();

        stability.Scorer.ShouldBe("meta_model");
        stability.Explained.ShouldBeTrue();
        stability.Features[0].Signs.ShouldBe([.. report.Results.Select(_ => 1)]);
        stability.Features[0].Verdict.ShouldBe(StabilityVerdict.Stable);
        complexity.Shifts.Count.ShouldBe(3);
        complexity.Shifts.ShouldAllBe(s => s.MaxPsi != null && s.Level != PsiLevel.Alarm); // one distribution throughout: at most noise on the watch side
        complexity.Ladder.Single(a => a.Scorer == "meta_model").Rung.ShouldBe(LadderRung.Trees);
        report.ToText().ShouldContain("complexity (evidence only, no verdict reads it)");
    }

    [Fact]
    public void The_evidence_changes_nothing_the_model_is_judged_on()
    {
        var rows = SyntheticRows.Make(1000, learnable: true);

        var off = WalkForwardValidator.Compare(rows, [new MlNetScorerFactory()], new ValidationOptions()).Value;
        var on = WalkForwardValidator.Compare(rows, [new MlNetScorerFactory()], new ValidationOptions(Ladder: new LadderOptions())).Value;

        off.Complexity.ShouldBeNull();
        on.Pooled.ShouldBe(off.Pooled);
        on.Results.Select(r => r.Scorers).ShouldBe(off.Results.Select(r => r.Scorers));
        on.Verdicts.Select(v => (v.Name, v.Outcome, v.FoldsBeatingBoth, v.PooledBeatsBoth, v.PooledEcePp, string.Join('|', v.Reasons)))
            .ShouldBe(off.Verdicts.Select(v => (v.Name, v.Outcome, v.FoldsBeatingBoth, v.PooledBeatsBoth, v.PooledEcePp, string.Join('|', v.Reasons))));
    }
}
