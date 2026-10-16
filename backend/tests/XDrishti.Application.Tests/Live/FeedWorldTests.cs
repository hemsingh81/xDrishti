using XDrishti.Application.Abstractions.Live;
using static XDrishti.Application.Tests.Live.FeedTestData;

namespace XDrishti.Application.Tests.Live;

/// <summary>
/// The test world of the connection manager, tested itself (ISS-14). A clock that is moved faster than the manager acts on it makes every test that depends on how much time passes between two turns of the manager - the 30 seconds of
/// silence, the window of the flapping rule - pass or fail with the speed of the machine, and always fail in a process that nothing has warmed up. These tests make the manager slow on purpose, which needs no sleep and no clock.
/// </summary>
public sealed class FeedWorldTests
{
    [Fact]
    public async Task The_clock_does_not_run_ahead_of_a_manager_that_is_slow_to_act_on_it()
    {
        using var world = new FeedWorld();
        world.Provider.Candidates.Add(Index(Nifty, "13")); // an index in the set: half a minute without a packet is silence
        await world.StartConnectedAsync();
        var start = world.Clock.GetUtcNow();
        var slowFirstTurn = world.Flush.HoldNextFlush(); // what a cold process does: the first turn of the manager takes much longer than the next ones

        var running = world.RunClockUntilAsync(() => world.Flush.Flushes >= 8);
        await FeedWorld.UntilAsync(() => world.Flush.Flushes == 1); // the manager is inside its slow first turn
        for (var turn = 0; turn < 10_000; turn++)
        {
            await Task.Yield(); // turns in which a clock that does not wait for the manager would move on by the hundred
        }

        (world.Clock.GetUtcNow() - start).ShouldBeLessThanOrEqualTo(2 * world.Settings.FlushInterval, "the first tick and, at most, the one the manager has not yet taken");
        slowFirstTurn.SetResult();
        await running;

        world.Connector.Attempts.ShouldBe(1); // the manager never saw half a minute pass without a packet
        world.Log.Events.ShouldNotContain(e => e.Event == 4113);
        await world.StopAsync();
    }

    [Fact]
    public async Task The_clock_goes_exactly_to_the_next_wake_up_of_the_manager_and_not_past_it()
    {
        using var world = new FeedWorld();
        world.Connector.AlwaysFail = FeedConnectFailureKind.Failed;
        var start = world.Clock.GetUtcNow();
        world.Begin();

        await world.RunClockUntilAsync(() => world.Connector.Attempts >= 4);

        (world.Clock.GetUtcNow() - start).ShouldBe(TimeSpan.FromSeconds(1 + 2 + 4)); // the back-offs after the first, second and third attempt, no more
        await world.StopAsync();
    }

    [Fact]
    public async Task A_tick_is_waited_for_until_the_manager_has_flushed_it_before_the_clock_moves_again()
    {
        using var world = new FeedWorld();
        AddTypicalSet(world);
        await world.StartConnectedAsync();

        await world.RunClockUntilAsync(() => world.Flush.Flushes >= 5);

        world.Flush.Flushes.ShouldBe(5); // one flush for every move of the clock, none missed, none piled up
        await world.StopAsync();
    }

    private static void AddTypicalSet(FeedWorld world) =>
        world.Provider.Candidates.AddRange([Holding(Reliance, "2885", 5000m), Index(Nifty, "13"), Alert(Infosys, "1594")]);
}
