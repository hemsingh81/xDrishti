using XDrishti.Domain.Common;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Tests.Protection;

/// <summary>Adversarial-review fixes of P12-04: no double sell from an unknown exit, a stop never rests under a flatten or after flat, a pending repair is not a failed one, a throwing event store cannot stop a halt request, exits are sell-only in the wire.</summary>
public sealed class ProtectionHardeningTests
{
    private static void ToIst(Rig rig, int hour, int minute) =>
        rig.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 26, hour, minute, 0, TimeSpan.Zero) - TimeSpan.FromMinutes(330));

    private static async Task<Rig> AtSquareOffAsync(ProtectionSettings? settings = null)
    {
        var rig = new Rig(settings);
        await rig.FilledAsync();
        await rig.StopAsync();
        ToIst(rig, 15, 15);
        rig.Source.Last = 1500m;
        return rig;
    }

    [Fact]
    public async Task An_exit_with_an_unknown_ack_is_not_sent_again_and_send_unknown_is_raised()
    {
        var rig = await AtSquareOffAsync();
        rig.Broker.NextOutcome = BrokerOutcome.Unknown;
        await rig.SquareOff.TickAsync(CancellationToken.None);
        var calls = rig.Broker.Calls;

        for (var i = 0; i < 20; i++)
        {
            rig.Clock.Advance(TimeSpan.FromSeconds(1));
            await rig.SquareOff.TickAsync(CancellationToken.None);
        }

        rig.Broker.Calls.ShouldBe(calls, "the outcome is unknown: no second exit");
        rig.IsOpen(AlarmCodes.SendUnknown).ShouldBeTrue();
    }

    [Fact]
    public async Task Negative_control_with_lookup_verified_on_the_unknown_exit_is_sent_again()
    {
        var rig = await AtSquareOffAsync(ProtectionSettings.Default with { LookupVerified = true });
        rig.Broker.NextOutcome = BrokerOutcome.Unknown;
        await rig.SquareOff.TickAsync(CancellationToken.None);
        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        await rig.SquareOff.TickAsync(CancellationToken.None);

        rig.OrdersOf(LegKind.Exit).ShouldBe(1);
    }

    [Fact]
    public async Task An_exit_sent_but_not_yet_in_a_book_read_after_it_is_not_sent_again_and_a_fresh_book_without_it_allows_one_more()
    {
        var stale = await AtSquareOffAsync();
        await stale.SquareOff.TickAsync(CancellationToken.None);
        stale.OrdersOf(LegKind.Exit).ShouldBe(1);
        stale.Source.BookOverride = [.. (await stale.Broker.GetOrderBookAsync(CancellationToken.None)).Value!.Where(o => !o.ClientOrderId!.EndsWith("X1", StringComparison.Ordinal))];
        for (var i = 0; i < 10; i++)
        {
            stale.Clock.Advance(TimeSpan.FromSeconds(1));
            stale.Source.BookAge = TimeSpan.FromSeconds(30); // read before the send
            await stale.SquareOff.TickAsync(CancellationToken.None);
        }

        stale.OrdersOf(LegKind.Exit).ShouldBe(1, "the book was read before the send: the exit may simply not be visible yet");

        var fresh = await AtSquareOffAsync();
        await fresh.SquareOff.TickAsync(CancellationToken.None);
        fresh.Source.BookOverride = [.. (await fresh.Broker.GetOrderBookAsync(CancellationToken.None)).Value!.Where(o => !o.ClientOrderId!.EndsWith("X1", StringComparison.Ordinal))];
        fresh.Clock.Advance(TimeSpan.FromSeconds(1));
        await fresh.SquareOff.TickAsync(CancellationToken.None);
        fresh.OrdersOf(LegKind.Exit).ShouldBe(1, "a minimum gap holds the next send back");
        fresh.Clock.Advance(TimeSpan.FromSeconds(5));
        await fresh.SquareOff.TickAsync(CancellationToken.None);
        fresh.OrdersOf(LegKind.Exit).ShouldBe(2, "negative control: with a fresh book that shows it absent and the gap passed, it is sent");
    }

    [Fact]
    public async Task A_partial_stop_is_cancelled_and_gone_from_the_book_before_the_flatten_is_sent_and_they_never_rest_together()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync(4);
        await rig.TickAsync(10);
        rig.Broker.RejectNextWith = "rejected"; // the repair for the other 6 fails
        var both = false;
        for (var i = 0; i < 12; i++)
        {
            await rig.TickAsync();
            var stopLive = rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)) == "PENDING";
            var flattenLive = rig.Broker.Legs.Any(l => l.Kind == LegKind.Flatten && rig.Broker.StatusOf(l.ClientOrderId) == "PENDING");
            both |= stopLive && flattenLive;
        }

        both.ShouldBeFalse();
        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("CANCELLED");
        rig.Broker.Legs.Where(l => l.Kind == LegKind.Flatten).ShouldAllBe(l => l.Qty == 10);
        rig.OrdersOf(LegKind.Flatten).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_repair_stop_with_an_unknown_outcome_is_pending_lookup_no_flatten_starts_and_nothing_is_sent_again()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.TickAsync(10);
        rig.Broker.NextOutcome = BrokerOutcome.Unknown;
        await rig.TickAsync(1);
        var calls = rig.Broker.Calls;
        await rig.TickAsync(60);

        rig.OrdersOf(LegKind.Flatten).ShouldBe(0);
        rig.Broker.Calls.ShouldBe(calls);
        rig.IsOpen(AlarmCodes.SendUnknown).ShouldBeTrue();
        rig.IsOpen(AlarmCodes.ProtectionBreach).ShouldBeTrue("the alarm stands");
    }

    [Fact]
    public async Task A_stop_left_resting_after_the_position_is_flat_is_cancelled()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        await rig.Broker.PlaceAsync(new BrokerLeg(Rig.Cid(LegKind.Exit, 1), LegKind.Exit, ExecSide.Sell, "1594", 10, 1400m), CancellationToken.None);
        rig.Broker.SetPrice("1594", 1500m); // the exit fills, the stop (trigger 1,485) does not
        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("PENDING");

        await rig.TickAsync(2);

        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("CANCELLED");
        rig.Events.State(361).ShouldBe(ExecState.Closed);
    }

    [Fact]
    public async Task A_stop_that_cannot_be_confirmed_gone_stops_the_flatten_with_exit_failed()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync(4);
        var sticky = (await rig.Broker.GetOrderBookAsync(CancellationToken.None)).Value!.Where(o => o.Side == ExecSide.Sell).ToList();
        rig.Source.BookOverride = [.. (await rig.Broker.GetOrderBookAsync(CancellationToken.None)).Value!]; // frozen copy: the stop stays "PENDING" whatever the cancel does
        rig.Broker.RejectNextWith = null;
        await rig.TickAsync(10);
        rig.Broker.RejectNextWith = "rejected";
        await rig.TickAsync(40);

        sticky.ShouldNotBeEmpty();
        rig.OrdersOf(LegKind.Flatten).ShouldBe(0);
        rig.IsOpen(AlarmCodes.ExitFailed).ShouldBeTrue();
    }

    private sealed class ThrowingEvents : IExecEventStore
    {
        public Task<Result> StartAsync(ExecIntent intent, DateOnly sessionDate, ExecEnvironment environment, string createdBy, CancellationToken cancellationToken) => throw new InvalidOperationException("down");

        public Task<Result> AppendAsync(long intentId, ExecEvent e, CancellationToken cancellationToken) => throw new InvalidOperationException("down");

        public Task<ExecIntentState?> StateAsync(long intentId, CancellationToken cancellationToken) => throw new InvalidOperationException("down");

        public Task<IReadOnlyList<ExecEvent>> EventsAsync(long intentId, CancellationToken cancellationToken) => throw new InvalidOperationException("down");

        public Task<Result<ExecIntent>> RebuildAsync(long intentId, CancellationToken cancellationToken) => throw new InvalidOperationException("down");
    }

    [Fact]
    public async Task A_throwing_event_store_cannot_keep_the_halt_request_from_being_made()
    {
        var alarms = new InMemoryAlarmStore();
        var halts = new RecordingHaltRequests();
        var report = new ProtectionReporter(alarms, halts, new ThrowingEvents(), TimeProvider.System);

        (await report.RaiseAsync(AlarmCodes.ProtectionBreach, Rig.Account, 361, "x", CancellationToken.None, HaltLevel.Entries)).ShouldBeTrue();
        (await report.MoveAsync(361, ExecState.Closed, CancellationToken.None)).ShouldBeFalse();

        halts.Requests.Count.ShouldBe(1);
        alarms.Open.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_halt_is_requested_again_while_the_alarm_stands_and_no_halt_is_in_force_and_not_once_one_is()
    {
        var alarms = new InMemoryAlarmStore();
        var halts = new RecordingHaltRequests();
        var report = new ProtectionReporter(alarms, halts, null, TimeProvider.System);

        await report.RaiseAsync(AlarmCodes.ProtectionBreach, null, 1, "x", CancellationToken.None, HaltLevel.Entries, null);
        await report.RaiseAsync(AlarmCodes.ProtectionBreach, null, 1, "x", CancellationToken.None, HaltLevel.Entries, null);
        halts.Requests.Count.ShouldBe(2, "still open, nothing in force: asked again");
        await report.RaiseAsync(AlarmCodes.ProtectionBreach, null, 1, "x", CancellationToken.None, HaltLevel.Entries, HaltLevel.Entries);
        halts.Requests.Count.ShouldBe(2);
        await report.RaiseAsync(AlarmCodes.Oversold, null, 1, "x", CancellationToken.None, HaltLevel.Freeze, HaltLevel.Entries);
        halts.Requests.Count.ShouldBe(3, "a milder halt in force does not satisfy a freeze request");
    }

    [Theory]
    [InlineData(LegKind.Exit)]
    [InlineData(LegKind.Flatten)]
    public void An_exit_or_flatten_to_buy_is_refused_in_the_wire(LegKind kind)
    {
        var leg = new BrokerLeg(Rig.Cid(kind, 1), kind, ExecSide.Buy, "1594", 10, 1490m);

        XDrishti.Execution.Dhan.DhanWire.Validate(leg).ShouldBe("exit_side");
        XDrishti.Execution.Dhan.DhanWire.Validate(leg with { Side = ExecSide.Sell }).ShouldBeNull();
    }
}
