using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests.Confirm;

/// <summary>
/// Spec P12-08 criterion 12 with P12-01's state machine (the in-memory event store) and a <see cref="LegSender"/> over the fake broker, composed the way a host would: the move checking to submitting happens only after the gate reads a
/// confirmed outcome at most 30 seconds old; an older one cancels the intent <c>expired_after_confirm</c> and nothing is sent; no ticket, an open one or any other outcome moves nothing; the closed default gate never opens.
/// </summary>
public sealed class ConfirmHandOffTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly DateOnly Session = new(2026, 10, 27);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Events : IExecEventStore
    {
        public InMemoryExecEventStore Store { get; } = new();

        public Task<Result> StartAsync(ExecIntent intent, DateOnly sessionDate, ExecEnvironment environment, string createdBy, CancellationToken cancellationToken) => Task.FromResult(Store.Start(intent));

        public Task<Result> AppendAsync(long intentId, ExecEvent e, CancellationToken cancellationToken) => Task.FromResult(Store.Append(intentId, e));

        public Task<ExecIntentState?> StateAsync(long intentId, CancellationToken cancellationToken) =>
            Task.FromResult(Store.Live(intentId) is { } live ? new ExecIntentState(live.State, live.Seq, live.FilledQty) : null);

        public Task<IReadOnlyList<ExecEvent>> EventsAsync(long intentId, CancellationToken cancellationToken) => Task.FromResult(Store.Events(intentId));

        public Task<Result<ExecIntent>> RebuildAsync(long intentId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Decisions(Func<ConfirmDecision?> read) : IConfirmDecisions
    {
        public Task<ConfirmDecision?> NewestAsync(long intentId, CancellationToken cancellationToken) => Task.FromResult(read());
    }

    private sealed class Open : IModeSource, IHaltSource, IPreSendCheck
    {
        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<ModeAnswer?>(new ModeAnswer(ExecMode.SemiAuto, true));

        public Task<HaltReading> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(HaltReading.Clear);

        public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) => Task.FromResult(GateDecision.Allowed);
    }

    private sealed class Journal : ISendJournal
    {
        public Task BeginAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DroppedAsync(BrokerLeg leg, string reason, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OutcomeAsync(BrokerLeg leg, BrokerAck ack, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record Scene(Events Events, FakeTimeProvider Clock, FakeBroker Broker, LegSender Sender, long IntentId)
    {
        public BrokerLeg Leg => new(ClientOrderId.Create(Session, IntentId, LegKind.Entry, 1).Value.ToString(), LegKind.Entry, ExecSide.Buy, "3426", 1, 412.35m);

        /// <summary>What a composing host would do: hand off, and send the entry once only when the intent moved to submitting.</summary>
        public async Task<HandOffResult> RunAsync(IConfirmGate gate)
        {
            var result = await new ConfirmHandOff(gate, Events, Clock).RunAsync(IntentId, Ct);
            if (result == HandOffResult.Submitting)
            {
                await Sender.SendAsync(new SendRequest(Account, IntentId, Leg, Clock.GetUtcNow()), Ct);
            }

            return result;
        }

        public ExecState State => Events.Store.Live(IntentId)!.State;

        public string? LastReason => JsonDocument.Parse(Events.Store.Events(IntentId)[^1].Payload).RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
    }

    private static Scene Checking()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 4, 16, 0, TimeSpan.Zero));
        var events = new Events();
        var intent = ExecIntent.Create(361, Guid.NewGuid(), Account, 501, ExecSide.Buy, "NSE:TATAPOWER", "3426", 1, 412.35m, 405.10m, 425.80m, ExecStructure.Super).Value;
        events.Store.Start(intent).IsSuccess.ShouldBeTrue();
        events.Store.Append(361, ExecEvent.Move(1, ExecState.Planned, ExecState.Armed, ExecEventSource.Engine, "system:intent-creator", clock.GetUtcNow())).IsSuccess.ShouldBeTrue();
        events.Store.Append(361, ExecEvent.Move(2, ExecState.Armed, ExecState.Checking, ExecEventSource.Engine, "system:intent-creator", clock.GetUtcNow())).IsSuccess.ShouldBeTrue();
        var broker = new FakeBroker(clock);
        var open = new Open();
        var sender = new LegSender(broker, open, open, open, new Journal(), new ExecRateLimiter(clock, 2, 30, 200), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(5));
        return new Scene(events, clock, broker, sender, 361);
    }

    private static StoredConfirmGate Gate(Scene s, string? outcome, double ageSeconds) =>
        new(new Decisions(() => new ConfirmDecision(Guid.NewGuid(), outcome, outcome is null ? null : s.Clock.GetUtcNow().AddSeconds(-ageSeconds), s.Clock.GetUtcNow())));

    [Fact]
    public async Task A_confirmation_10_seconds_old_moves_the_intent_to_submitting_and_the_entry_is_sent_once()
    {
        var s = Checking();
        var gate = Gate(s, TicketOutcomes.Confirmed, 10);

        var first = await s.RunAsync(gate);
        var calls = s.Broker.Calls;
        var second = await s.RunAsync(gate);

        (first, second).ShouldBe((HandOffResult.Submitting, HandOffResult.Wait));
        s.State.ShouldBe(ExecState.Submitting);
        s.LastReason.ShouldBe(TicketOutcomes.Confirmed);
        calls.ShouldBeGreaterThan(0);
        s.Broker.Calls.ShouldBe(calls, "a second pass sends nothing more");
    }

    [Fact]
    public async Task A_confirmation_older_than_30_seconds_cancels_the_intent_expired_after_confirm_and_sends_nothing()
    {
        var s = Checking();

        var result = await s.RunAsync(Gate(s, TicketOutcomes.Confirmed, 30.001));

        result.ShouldBe(HandOffResult.Cancelled);
        s.State.ShouldBe(ExecState.Cancelled);
        s.LastReason.ShouldBe(ConfirmPolicy.ExpiredAfterConfirm);
        s.Broker.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, "awaiting_tap")]
    [InlineData(TicketOutcomes.Skipped, TicketOutcomes.Skipped)]
    [InlineData(TicketOutcomes.Expired, TicketOutcomes.Expired)]
    [InlineData(TicketOutcomes.Refused, TicketOutcomes.Refused)]
    public async Task An_open_ticket_or_any_other_outcome_moves_nothing_and_sends_nothing(string? outcome, string reason)
    {
        var s = Checking();
        var gate = Gate(s, outcome, 1);

        (await gate.CheckAsync(s.IntentId, Ct)).ShouldBe(new GateDecision(false, reason));
        (await s.RunAsync(gate)).ShouldBe(HandOffResult.Wait);
        s.State.ShouldBe(ExecState.Checking);
        s.Broker.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task No_ticket_an_unreadable_record_and_the_closed_default_gate_never_open()
    {
        var s = Checking();

        (await new StoredConfirmGate(new Decisions(() => null)).CheckAsync(1, Ct)).ShouldBe(new GateDecision(false, "no_ticket"));
        (await new StoredConfirmGate(new Decisions(() => throw new InvalidOperationException("down"))).CheckAsync(1, Ct)).ShouldBe(new GateDecision(false, "confirm_unreadable"));
        (await s.RunAsync(new ClosedConfirmGate())).ShouldBe(HandOffResult.Wait);
        s.State.ShouldBe(ExecState.Checking);
        s.Broker.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task The_freshness_bound_is_30_seconds_by_the_database_clock_inclusive()
    {
        var s = Checking();

        (await Gate(s, TicketOutcomes.Confirmed, 30).CheckAsync(1, Ct)).Open.ShouldBeTrue();
        (await Gate(s, TicketOutcomes.Confirmed, 30.001).CheckAsync(1, Ct)).ShouldBe(new GateDecision(false, ConfirmPolicy.ExpiredAfterConfirm));
        (await Gate(s, TicketOutcomes.Confirmed, -5).CheckAsync(1, Ct)).Open.ShouldBeFalse("a decision from the future is not trusted");
    }

    [Fact]
    public async Task An_intent_that_is_not_checking_is_never_handed_off()
    {
        var s = Checking();
        s.Events.Store.Append(s.IntentId, ExecEvent.Move(3, ExecState.Checking, ExecState.Cancelled, ExecEventSource.Engine, "system:x", s.Clock.GetUtcNow())).IsSuccess.ShouldBeTrue();

        (await s.RunAsync(Gate(s, TicketOutcomes.Confirmed, 1))).ShouldBe(HandOffResult.Wait);
        s.Broker.Calls.ShouldBe(0);
    }
}
