using XDrishti.Application.Replay;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Replay;

/// <summary>SR-02, AC 2 and 6: with one open slot the selection arm takes one of the candidates, the baseline arm takes all of them through the same fill path, and the arm is part of the stored parameters and so of the input hash.</summary>
public sealed class ReplayBaselineArmTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FillConstraints OneSlot() =>
        new(MaxEntriesPerDay: 1, MaxOpenPositions: 1, MaxTotalOpenRiskPct: 3.0, MaxPerSector: 10, MaxPairCorrelation: 1.0, MaxIntraday: 10, MaxSwing: 10, MaxNetLongRiskPct: 50, MaxNetShortRiskPct: 50, MaxPerTradeRiskPct: 2.0);

    private static ReplayWorld World()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB", "CCC");
        world.Fill = OneSlot();
        var d1 = new DateOnly(2026, 10, 5);
        world.Series("AAA", d => d < d1 ? 99.9 : 100.0);
        world.Series("BBB", d => d < d1 ? 99.9 : 100.0);
        world.Series("CCC", d => d < d1 ? 99.9 : 100.0);
        return world;
    }

    [Fact]
    public async Task The_baseline_takes_every_candidate_the_selection_arm_limits_and_blocks_by_no_selection_reason()
    {
        var selection = await ReplayRig.Create(World()).Driver.RunAsync(ReplayRig.Params(2), Ct);
        var baseline = await ReplayRig.Create(World()).Driver.RunAsync(ReplayRig.Params(2) with { Arm = ReplayArm.Baseline }, Ct);

        selection.IsSuccess.ShouldBeTrue();
        baseline.IsSuccess.ShouldBeTrue();
        var first = ReplayWorld.Start;
        var s = selection.Value.Sessions.Single(x => x.Session == first);
        var b = baseline.Value.Sessions.Single(x => x.Session == first);
        b.Candidates.Count.ShouldBe(s.Candidates.Count);
        s.Taken.Count.ShouldBeLessThan(b.Candidates.Count); // the one slot limits the selection arm
        b.Taken.Count.ShouldBe(b.Candidates.Count);
        b.Fills.Count(f => f.Status == ReplayFill.Filled).ShouldBe(b.Candidates.Count);
        b.Blocked.Select(x => x.Reason).ShouldNotContain(BlockReason.SmartRule);
        b.Blocked.Select(x => x.Reason).ShouldNotContain(BlockReason.MaxPositions);
        b.Blocked.Select(x => x.Reason).ShouldNotContain(BlockReason.ClusterCap);
        b.Labels.ShouldNotBeNull();
    }

    [Fact]
    public void The_arm_is_part_of_the_stored_parameters_and_a_selection_run_keeps_its_old_text()
    {
        var p = ReplayRig.Params(2);

        ReplayJson.Canonical(p).ShouldNotContain("arm");
        ReplayJson.Canonical(p with { Arm = ReplayArm.Baseline }).ShouldContain("\"arm\":\"Baseline\"");
        ReplayJson.Read<ReplayParams>(ReplayJson.Canonical(p with { Arm = ReplayArm.Baseline }))!.Arm.ShouldBe(ReplayArm.Baseline);
        ReplayJson.Read<ReplayParams>(ReplayJson.Canonical(p))!.Arm.ShouldBe(ReplayArm.Selection);
        ReplayJson.Sha256(ReplayJson.Canonical(p)).ShouldNotBe(ReplayJson.Sha256(ReplayJson.Canonical(p with { Arm = ReplayArm.Baseline })));
    }
}
