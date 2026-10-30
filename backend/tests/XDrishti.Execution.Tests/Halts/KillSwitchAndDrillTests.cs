using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Tests.Protection;

namespace XDrishti.Execution.Tests.Halts;

internal sealed class FakeKillSwitch : IBrokerKillSwitch
{
    public int Calls { get; private set; }

    public BrokerOutcome Outcome { get; set; } = BrokerOutcome.Acknowledged;

    public bool Throws { get; set; }

    public Task<BrokerAck> ActivateAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (Throws)
        {
            throw new InvalidOperationException("boom");
        }

        return Task.FromResult(new BrokerAck(Outcome, null, Outcome.ToString()));
    }
}

/// <summary>P12-05 criterion 8: the broker's switch, on a fake handler only (UNVERIFIED against Dhan, nothing implements the port).</summary>
public sealed class KillSwitchCoordinatorTests
{
    private static ActiveHalt OwnerHalt(bool kill = false, string code = "owner_button") => new(1, DateTimeOffset.UnixEpoch, HaltScope.Account, Rig.Account, HaltLevel.Cancel, code, "owner", "owner:t", "x", kill);

    private static (KillSwitchCoordinator Coordinator, FakeKillSwitch Switch, InMemoryKillSwitchLog Log, Rig Rig) Build(bool setting)
    {
        var rig = new Rig();
        var fake = new FakeKillSwitch();
        var log = new InMemoryKillSwitchLog();
        return (new KillSwitchCoordinator(fake, log, rig.Source, () => setting, rig.Clock), fake, log, rig);
    }

    [Fact]
    public async Task Off_by_default_and_off_for_the_halt_nothing_is_called()
    {
        var (coordinator, fake, log, rig) = Build(setting: false);
        rig.Source.State = ExecState.Closed;

        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);

        fake.Calls.ShouldBe(0);
        (await log.OutcomesAsync(1, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task With_a_position_open_it_records_deferred_once_and_does_not_call_and_when_flat_it_calls_exactly_once()
    {
        var (coordinator, fake, log, rig) = Build(setting: true);
        await rig.FilledAsync();
        await rig.StopAsync();

        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);
        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);

        fake.Calls.ShouldBe(0);
        (await log.OutcomesAsync(1, CancellationToken.None)).ShouldBe(["deferred"], "recorded once");

        rig.Broker.EndOfDay();
        await rig.Broker.PlaceAsync(new BrokerLeg(Rig.Cid(LegKind.Exit, 1), LegKind.Exit, ExecSide.Sell, "1594", 10, 1490m), CancellationToken.None);
        rig.Broker.SetPrice("1594", 1495m);
        rig.Source.State = ExecState.Closed;
        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);
        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);

        fake.Calls.ShouldBe(1);
        (await log.OutcomesAsync(1, CancellationToken.None)).ShouldBe(["deferred", "activated"]);
    }

    [Fact]
    public async Task A_call_that_throws_is_recorded_as_failed_without_content_and_never_repeated()
    {
        var (coordinator, fake, log, rig) = Build(setting: true);
        rig.Source.State = ExecState.Closed;
        fake.Throws = true;

        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);
        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);

        fake.Calls.ShouldBe(1);
        (await log.OutcomesAsync(1, CancellationToken.None)).ShouldBe(["failed"]);
    }

    [Fact]
    public async Task The_halts_own_choice_switches_it_on_without_the_setting()
    {
        var (coordinator, fake, _, rig) = Build(setting: false);
        rig.Source.State = ExecState.Closed;

        await coordinator.TickAsync([OwnerHalt(kill: true)], CancellationToken.None);

        fake.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("feed_loss")]
    [InlineData("clock_skew")]
    [InlineData("broker_outage")]
    [InlineData("protection_unknown")]
    [InlineData("token_invalid")]
    public async Task A_trigger_that_clears_itself_never_activates_the_switch_even_flat_and_even_with_the_setting_on(string code)
    {
        var (coordinator, fake, _, rig) = Build(setting: true);
        rig.Source.State = ExecState.Closed;

        await coordinator.TickAsync([OwnerHalt(code: code)], CancellationToken.None);

        fake.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_unreadable_book_is_not_flat_and_a_failed_or_unknown_answer_is_recorded_and_never_repeated()
    {
        var (coordinator, fake, log, rig) = Build(setting: true);
        rig.Source.State = ExecState.Closed;
        rig.Source.Unknown = true;
        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);
        fake.Calls.ShouldBe(0);

        rig.Source.Unknown = false;
        fake.Outcome = BrokerOutcome.Unknown;
        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);
        await coordinator.TickAsync([OwnerHalt()], CancellationToken.None);

        fake.Calls.ShouldBe(1);
        (await log.OutcomesAsync(1, CancellationToken.None)).ShouldBe(["deferred", "failed"]);
    }
}

/// <summary>P12-05 criterion 10: the drill proves the gate with a refusing transport and sends nothing.</summary>
public sealed class DrillTests
{
    private static (DrillService Drill, InMemoryHaltStore Store, InMemoryDrillStore Drills, Rig Rig) Build()
    {
        var rig = new Rig();
        var store = new InMemoryHaltStore();
        var drills = new InMemoryDrillStore();
        rig.Source.State = ExecState.Closed;
        return (new DrillService(new HaltService(store, rig.Clock), store, rig.Source, drills, rig.Clock), store, drills, rig);
    }

    [Fact]
    public async Task A_drill_on_a_flat_account_raises_a_cancel_halt_stops_the_entry_at_the_gate_clears_itself_and_stores_a_passing_row()
    {
        var (drill, store, drills, _) = Build();

        var result = await drill.RunAsync(Rig.Account, CancellationToken.None);

        result.Passed.ShouldBeTrue(result.Detail);
        result.Detail.ShouldContain("transport_calls=0");
        result.Detail.ShouldContain("entry_stopped=True");
        (await store.ActiveAsync(CancellationToken.None)).ShouldBeEmpty("the drill's own halt is cleared");
        store.AutoClears.Single().Code.ShouldBe("drill");
        drills.Rows.Single().Passed.ShouldBeTrue();
        store.Rows.ShouldBe(1);
    }

    [Fact]
    public async Task If_something_after_the_halt_throws_the_drills_own_halt_is_still_cleared()
    {
        var (_, store, drills, rig) = Build();
        var throwing = new DrillService(new HaltService(store, rig.Clock), store, rig.Source, new ThrowingDrills(), rig.Clock);

        await Should.ThrowAsync<InvalidOperationException>(() => throwing.RunAsync(Rig.Account, CancellationToken.None));

        (await store.ActiveAsync(CancellationToken.None)).ShouldBeEmpty();
        store.AutoClears.Single().Code.ShouldBe("drill");
        drills.Rows.ShouldBeEmpty();
    }

    private sealed class ThrowingDrills : IDrillStore
    {
        public Task AppendAsync(DrillResult result, CancellationToken cancellationToken) => throw new InvalidOperationException("store down");
    }

    [Fact]
    public async Task A_drill_refuses_when_a_position_or_a_working_order_exists_or_any_other_halt_is_active_and_changes_nothing()
    {
        var withPosition = Build();
        withPosition.Rig.Source.State = ExecState.Filled;
        await withPosition.Rig.FilledAsync();
        (await withPosition.Drill.RunAsync(Rig.Account, CancellationToken.None)).Detail.ShouldBe("refused:not_flat");
        withPosition.Store.Rows.ShouldBe(0);

        var withHalt = Build();
        await withHalt.Store.AppendAsync(new HaltDraft(HaltScope.System, null, HaltLevel.Entries, HaltTriggers.ExchangeHalt, "system", "system:t", "x", DateTimeOffset.UnixEpoch), CancellationToken.None);
        (await withHalt.Drill.RunAsync(Rig.Account, CancellationToken.None)).Detail.ShouldBe("refused:halt_active");
        withHalt.Store.Rows.ShouldBe(1);

        var unreadable = Build();
        unreadable.Store.Unreadable = true;
        (await unreadable.Drill.RunAsync(Rig.Account, CancellationToken.None)).Detail.ShouldBe("refused:halts_unreadable");

        var blind = Build();
        blind.Rig.Source.Unknown = true;
        (await blind.Drill.RunAsync(Rig.Account, CancellationToken.None)).Detail.ShouldBe("refused:no_snapshot");
        blind.Drills.Rows.ShouldAllBe(r => !r.Passed);
    }

    [Fact]
    public async Task The_refusing_transport_counts_every_call_so_a_gate_that_let_an_entry_through_would_fail_the_drill()
    {
        var transport = new RefusingBrokerOrders();
        var clock = new FakeTimeProvider();
        var open = new LegSender(transport, new FixedMode(new ModeAnswer(ExecMode.SemiAuto, true)), new ClearHalts(), new AllowAllPreSend(), new NullJournal(), new ExecRateLimiter(clock, 5, 5, 5), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(5));

        var result = await open.SendAsync(new SendRequest(Rig.Account, 1, Rig.Entry(), clock.GetUtcNow()), CancellationToken.None);

        result.Status.ShouldBe(SendStatus.Sent, "with no halt the entry reaches the transport");
        result.Ack!.Outcome.ShouldBe(BrokerOutcome.NotSent);
        transport.Calls.ShouldBe(1);
    }

    private sealed class AllowAllPreSend : IPreSendCheck
    {
        public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) => Task.FromResult(GateDecision.Allowed);
    }
}
