using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Sim;

namespace XDrishti.Application.Tests.Replay.Sim;

/// <summary>SR-04 (ADR 0293): the widened clamp of a simulation inside hold-out block 1. Without it the clamp stays at the cut; with it a read past <c>through</c> still fails loudly, and the days inside the block carry <c>seen_holdout</c>.</summary>
public sealed class SimDaysHoldOutReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly BlockStart = new(2026, 10, 8); // the fourth session of the run

    private static ReplayWorld World()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB", "CCC", "DDD");
        world.Rising("AAA", 0.7);
        world.Rising("BBB", 2.0);
        world.Rising("CCC", 3.3);
        world.Rising("DDD", 4.9);
        world.HoldOutStart = BlockStart;
        world.HoldOutEnd = new DateOnly(2026, 11, 30);
        world.EmbargoDays = 0;
        return world;
    }

    [Fact]
    public async Task Without_the_widening_the_clamp_stays_at_the_cut()
    {
        var facts = World().Facts();

        (await facts.LastReadableSessionAsync(Ct)).ShouldBe(BlockStart.AddDays(-1));
        await Should.ThrowAsync<ReplayHoldOutException>(async () => await facts.MinuteBarsAsync(ReplayWorld.Ids["AAA"], BlockStart, Ct));
        (await facts.ReleasedHoldOutAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Widened_it_reads_up_to_through_and_a_read_past_through_still_throws()
    {
        var facts = World().Facts();
        var through = new DateOnly(2026, 10, 12);

        facts.ReadInsideHoldOut(through).ShouldBeTrue();

        (await facts.LastReadableSessionAsync(Ct)).ShouldBe(through);
        (await facts.MinuteBarsAsync(ReplayWorld.Ids["AAA"], through, Ct)).ShouldNotBeEmpty();
        await Should.ThrowAsync<ReplayHoldOutException>(async () => await facts.MinuteBarsAsync(ReplayWorld.Ids["AAA"], through.AddDays(1), Ct));
        (await facts.ReleasedHoldOutAsync(Ct)).ShouldBe([(BlockStart, new DateOnly(2026, 11, 30))]);
    }

    [Fact]
    public async Task A_simulation_inside_block_one_labels_its_days_there_seen_holdout()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(6, readThrough: ReplayWorld.RunSessions[5]);
        (await new SetSimAutoHandler(rig.Store, rig.Calendar, rig.Options, rig.Time).HandleAsync(new SetSimAutoCommand(id, true), Ct)).IsSuccess.ShouldBeTrue();

        await rig.Runner().TickAsync(Ct);

        var days = rig.Store.Days[id];
        rig.Store.Row(id).Phase.ShouldBe(SimDays.Complete);
        days.Count.ShouldBe(6);
        foreach (var day in days)
        {
            var labels = ReplayJson.Read<Domain.Replay.ReplaySessionRecord>(day.RecordJson)!.Labels;
            labels.Any(l => l.StartsWith("seen_holdout", StringComparison.Ordinal)).ShouldBe(day.Session >= BlockStart, day.Session.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
