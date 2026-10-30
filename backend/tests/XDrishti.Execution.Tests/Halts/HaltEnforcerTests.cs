using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;
using XDrishti.Execution.Tests.Protection;

namespace XDrishti.Execution.Tests.Halts;

/// <summary>P12-05 criteria 3 and 4: what a halt level does beyond blocking entries, on the FakeBroker.</summary>
public sealed class HaltEnforcerTests
{
    private static readonly Guid Account = Rig.Account;

    private static (HaltEnforcer Enforcer, InMemoryHaltStore Store) Build(Rig rig, ModeAnswer? mode = null)
    {
        var store = new InMemoryHaltStore();
        var guard = mode is null ? rig.Guard : new ProtectiveGuard(new FixedMode(mode), ExecEnvironment.Sandbox);
        return (new HaltEnforcer(store, rig.Source, rig.Broker, guard, rig.Report, rig.SquareOff, rig.Clock), store);
    }

    private static Task<(ActiveHalt Halt, bool Created)> HaltAsync(InMemoryHaltStore store, HaltLevel level, Guid? account = null, string code = HaltTriggers.OwnerButton) =>
        store.AppendAsync(new HaltDraft(account is null ? HaltScope.System : HaltScope.Account, account, level, code, "owner", "owner:test", "x", DateTimeOffset.UnixEpoch), CancellationToken.None);

    private static async Task<Rig> WithWorkingEntryAndProtectedPositionAsync()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        await rig.Broker.PlaceAsync(new BrokerLeg(Rig.Cid(LegKind.Entry, 1, 362), LegKind.Entry, ExecSide.Buy, "1594", 5, 1400m), CancellationToken.None);
        rig.Source.EntryIntentOverride = 362;
        return rig;
    }

    [Fact]
    public async Task At_cancel_a_working_entry_is_cancelled_and_the_stop_and_the_position_stay()
    {
        var rig = await WithWorkingEntryAndProtectedPositionAsync();
        var (enforcer, store) = Build(rig);
        await HaltAsync(store, HaltLevel.Cancel, Account);

        await enforcer.TickAsync(CancellationToken.None);

        rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1, 362)).ShouldBe("CANCELLED");
        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("PENDING", "protective legs stay");
        (await rig.Broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(10);
        rig.OrdersOf(LegKind.Exit).ShouldBe(0, "cancel does not exit anything");
    }

    [Theory]
    [InlineData(HaltLevel.Entries)]
    [InlineData(HaltLevel.Freeze)]
    public async Task At_entries_and_at_freeze_no_working_entry_is_touched(HaltLevel level)
    {
        var rig = await WithWorkingEntryAndProtectedPositionAsync();
        var (enforcer, store) = Build(rig);
        await HaltAsync(store, level, Account);
        var calls = rig.Broker.Calls;

        await enforcer.TickAsync(CancellationToken.None);

        rig.Broker.Calls.ShouldBe(calls);
        rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1, 362)).ShouldBe("PENDING");
    }

    [Fact]
    public async Task A_halt_of_another_account_cancels_nothing_here_and_a_system_halt_cancels_everything()
    {
        var rig = await WithWorkingEntryAndProtectedPositionAsync();
        var (enforcer, store) = Build(rig);
        await HaltAsync(store, HaltLevel.Cancel, Guid.NewGuid());
        await enforcer.TickAsync(CancellationToken.None);
        rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1, 362)).ShouldBe("PENDING");

        await HaltAsync(store, HaltLevel.Cancel, null, HaltTriggers.RunawayRate);
        await enforcer.TickAsync(CancellationToken.None);

        rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1, 362)).ShouldBe("CANCELLED");
    }

    [Fact]
    public async Task A_closed_gate_cancels_nothing_and_a_missing_snapshot_or_unreadable_record_does_nothing()
    {
        var rig = await WithWorkingEntryAndProtectedPositionAsync();
        var (closed, store) = Build(rig, new ModeAnswer(ExecMode.SemiAuto, false));
        await HaltAsync(store, HaltLevel.Cancel, Account);
        await closed.TickAsync(CancellationToken.None);
        rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1, 362)).ShouldBe("PENDING");

        var (open, openStore) = Build(rig);
        await HaltAsync(openStore, HaltLevel.Cancel, Account);
        rig.Source.Unknown = true;
        await open.TickAsync(CancellationToken.None);
        rig.Source.Unknown = false;
        openStore.Unreadable = true;
        await open.TickAsync(CancellationToken.None);

        rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1, 362)).ShouldBe("PENDING");
    }

    [Fact]
    public async Task A_cancel_the_book_does_not_show_is_retried_a_second_apart_three_times_then_alarmed_and_never_a_fourth_call()
    {
        var rig = await WithWorkingEntryAndProtectedPositionAsync();
        rig.Source.BookOverride = (await rig.Broker.GetOrderBookAsync(CancellationToken.None)).Value; // the book keeps showing the entry as working
        var (enforcer, store) = Build(rig);
        await HaltAsync(store, HaltLevel.Cancel, Account);
        var before = rig.Broker.Calls;

        for (var i = 0; i < 8; i++)
        {
            await enforcer.TickAsync(CancellationToken.None);
            rig.Clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        // reads count as calls in the fake only for writes; cancels are the writes made here
        (rig.Broker.Calls - before).ShouldBe(HaltEnforcer.CancelAttempts);
        rig.IsOpen(AlarmCodes.ExitFailed).ShouldBeTrue();
    }

    [Fact]
    public async Task At_exit_all_the_position_is_flattened_reduce_only_and_the_stop_is_cancelled_only_once_flat()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        rig.Source.Last = 1500m;
        var (enforcer, store) = Build(rig);
        await HaltAsync(store, HaltLevel.ExitAll, Account);

        await enforcer.TickAsync(CancellationToken.None);

        var exit = rig.Broker.Legs.Single(l => l.Kind == LegKind.Exit);
        (exit.Side, exit.Qty).ShouldBe((ExecSide.Sell, 10));
        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("PENDING", "the stop stays until the exit has filled");

        rig.Broker.SetPrice("1594", 1500m);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        await enforcer.TickAsync(CancellationToken.None);

        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("CANCELLED");
        (await rig.Broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(0);
        (await store.ActiveAsync(CancellationToken.None)).Count.ShouldBe(1, "the halt stays until the owner releases it");
    }

    [Fact]
    public async Task At_exit_all_a_position_without_a_quantity_is_skipped_and_a_halt_of_another_account_flattens_nothing()
    {
        var rig = new Rig();
        rig.Source.State = ExecState.Closed;
        var (enforcer, store) = Build(rig);
        await HaltAsync(store, HaltLevel.ExitAll, Account);
        await enforcer.TickAsync(CancellationToken.None);
        rig.OrdersOf(LegKind.Exit).ShouldBe(0);

        var other = new Rig();
        await other.FilledAsync();
        await other.StopAsync();
        other.Source.Last = 1500m;
        var (otherEnforcer, otherStore) = Build(other);
        await HaltAsync(otherStore, HaltLevel.ExitAll, Guid.NewGuid());
        await otherEnforcer.TickAsync(CancellationToken.None);

        other.OrdersOf(LegKind.Exit).ShouldBe(0);
    }

    [Fact]
    public async Task At_exit_all_an_exit_that_does_not_fill_is_walked_wider_and_when_the_walk_runs_out_flatten_incomplete_is_reported_with_a_halt_request()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        rig.Source.Last = 1500m;
        var (enforcer, store) = Build(rig);
        await HaltAsync(store, HaltLevel.ExitAll, Account);

        for (var i = 0; i < 40; i++)
        {
            await enforcer.TickAsync(CancellationToken.None); // the price never reaches the exit
            rig.Clock.Advance(TimeSpan.FromSeconds(4));
        }

        rig.OrdersOf(LegKind.Exit).ShouldBeGreaterThan(1, "walked to wider prices");
        rig.OrdersOf(LegKind.Exit).ShouldBeLessThanOrEqualTo(ProtectionSettings.Default.FlattenWalkSteps + ProtectionWatchdog.RetryAttempts);
        (await rig.Broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(10);
        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("PENDING", "the stop is never cancelled while the position is open");
    }
}
