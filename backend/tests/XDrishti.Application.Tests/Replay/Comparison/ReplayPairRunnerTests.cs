using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Comparison;
using XDrishti.Domain.Replay.Comparison;

namespace XDrishti.Application.Tests.Replay.Comparison;

public sealed class ReplayPairRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_runs_over_the_part_A_driver_are_paired_by_session_and_repeat()
    {
        var world = new ReplayWorld().WithDefaults("AAA");
        world.Rising("AAA", 0.7);
        var rig = ReplayRig.Create(world);
        var runner = new ReplayPairRunner(rig.Driver);
        var p = ReplayRig.Params(5);

        var a = await runner.RunAsync(p, Ct);

        a.IsSuccess.ShouldBeTrue(a.IsFailure ? a.Error.Message : string.Empty);
        a.Value.Frozen.Sessions.Count.ShouldBe(5);
        a.Value.Learning.Sessions.Select(s => s.Session).ShouldBe(a.Value.Frozen.Sessions.Select(s => s.Session));
        a.Value.Comparison.Days.ShouldBe(5);
        a.Value.Comparison.Answer.ShouldBe(PairAnswer.TooLittleHistory); // 5 days is under the bootstrap's 20
        (await runner.RunAsync(p, Ct)).Value.Comparison.ShouldBe(a.Value.Comparison);
    }

    [Fact]
    public async Task A_failed_run_is_returned_as_a_failure()
    {
        var rig = ReplayRig.Create(new ReplayWorld().WithDefaults("AAA"));
        var bad = ReplayRig.Params(5) with { Capital = 0 };
        (await new ReplayPairRunner(rig.Driver).RunAsync(bad, Ct)).Error.Code.ShouldBe(ReplayDriver.InvalidCode);
    }
}
