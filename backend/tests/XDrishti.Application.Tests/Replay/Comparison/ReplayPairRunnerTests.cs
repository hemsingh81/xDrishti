#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Comparison;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Replay.Comparison;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Replay.Comparison;

public sealed class ReplayPairRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async IAsyncEnumerable<RunTrade> ToAsync(IEnumerable<RunTrade> trades)
    {
        foreach (var t in trades)
        {
            yield return t;
        }

        await Task.CompletedTask;
    }

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
    public async Task Over_a_long_range_where_learning_changes_the_cells_the_pair_reaches_the_comparison_with_its_counts()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "CCC");
        world.Series("AAA", d => 100 + (3 * (d.DayNumber % 4))); // a sawtooth: positions are filled and leave within a few sessions, so equity moves
        world.Series("CCC", d => 100 + (3 * ((d.DayNumber + 1) % 4)));
        world.Snapshots.Add((new DateOnly(2026, 10, 30), ReplayWorld.Stats(new DateOnly(2026, 10, 30), 99) with { Sources = "newrun:swing:FIXED_RR{rr=2}" }));
        var trades = new[] { new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 14) }
            .Select(d => new RunTrade(FabricatedRun.Trade(ReplayWorld.Ids["AAA"], "AAA", Side.Buy, d, strategy: "S1") with { TriggerTimeframe = Timeframe.D1 }, [])).ToList();
        world.Runs.FindAsync("newrun", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<XDrishti.Application.Abstractions.Research.StoredRunHeader?>(ReplayWorld.Blank<XDrishti.Application.Abstractions.Research.StoredRunHeader>()));
        world.Runs.StreamTradesAsync("newrun", false, Arg.Any<CancellationToken>()).Returns(_ => ToAsync(trades));
        var rig = ReplayRig.Create(world);

        var r = await new ReplayPairRunner(rig.Driver).RunAsync(ReplayRig.Params(25), Ct);

        r.IsSuccess.ShouldBeTrue(r.IsFailure ? r.Error.Message : string.Empty);
        r.Value.Comparison.Days.ShouldBe(25);
        r.Value.Comparison.InformativeDays.ShouldBeLessThanOrEqualTo(25);
        r.Value.Learning.Sessions.Select(s => s.Equity).ShouldNotBe(r.Value.Frozen.Sessions.Select(s => s.Equity));
    }

    [Fact]
    public async Task A_failed_run_is_returned_as_a_failure()
    {
        var rig = ReplayRig.Create(new ReplayWorld().WithDefaults("AAA"));
        var bad = ReplayRig.Params(5) with { Capital = 0 };
        (await new ReplayPairRunner(rig.Driver).RunAsync(bad, Ct)).Error.Code.ShouldBe(ReplayDriver.InvalidCode);
    }
}
