using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Tests.Confirm;

/// <summary>
/// The P12-03 alignment with P12-08 criterion 12 over P12-01's state machine (the in-memory event store): <see cref="CheckRejectionMover"/> moves <c>checking&gt;rejected</c> with the codes when the rejection is final and leaves a Soft-only
/// rejection in <c>checking</c> only while its ticket is open; it moves nothing it cannot read, nothing that is not checking, and loses any race on the event chain without harm; a rejected intent is never handed off.
/// </summary>
public sealed class CheckRejectionMoverTests
{
    private static readonly Guid Account = Guid.NewGuid();
    private const long IntentId = 361;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Events : IExecEventStore
    {
        public InMemoryExecEventStore Store { get; } = new();

        public Task<Result> StartAsync(ExecIntent intent, DateOnly sessionDate, ExecEnvironment environment, string createdBy, CancellationToken cancellationToken) => Task.FromResult(Store.Start(intent));

        public bool AppendThrows { get; set; }

        public Task<Result> AppendAsync(long intentId, ExecEvent e, CancellationToken cancellationToken) =>
            AppendThrows ? throw new InvalidOperationException("db down at the move") : Task.FromResult(Store.Append(intentId, e));

        public Task<ExecIntentState?> StateAsync(long intentId, CancellationToken cancellationToken) =>
            Task.FromResult(Store.Live(intentId) is { } live ? new ExecIntentState(live.State, live.Seq, live.FilledQty) : null);

        public Task<IReadOnlyList<ExecEvent>> EventsAsync(long intentId, CancellationToken cancellationToken) => Task.FromResult(Store.Events(intentId));

        public Task<Result<ExecIntent>> RebuildAsync(long intentId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Scene : IRejectionReads, IModeSource, IConfirmTicketStore
    {
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 27, 4, 16, 0, TimeSpan.Zero));

        public Events Events { get; } = new();

        public RejectionRun? Run { get; set; } = new(91, CheckVerdict.Rejected, [], [CheckCodes.AdvShare]);

        public RejectionTicket? Ticket { get; set; }

        public bool Unreadable { get; set; }

        public ModeAnswer? Mode { get; set; } = new(ExecMode.SemiAuto, true);

        /// <summary>Runs at each read of the record (before the mover decides and appends), to stage a race on the event chain.</summary>
        public Action? BeforeRead { get; set; }

        /// <summary>Runs inside the close (refusal or expiry) before the primary key decides, to stage the owner's tap, the expiry loop or a second ticket racing it.</summary>
        public Action? OnRefuse { get; set; }

        public bool RefuseThrows { get; set; }

        public bool ModeThrows { get; set; }

        public int ModeReads { get; private set; }

        public List<(Guid Ticket, string Reason, string Actor)> Refusals { get; } = [];

        public List<(Guid Ticket, string Actor)> Expirations { get; } = [];

        public Scene()
        {
            var intent = ExecIntent.Create(IntentId, Guid.NewGuid(), Account, 501, ExecSide.Buy, "NSE:TATAPOWER", "3426", 1, 412.35m, 405.10m, 425.80m, ExecStructure.Super).Value;
            Events.Store.Start(intent).IsSuccess.ShouldBeTrue();
            Events.Store.Append(IntentId, ExecEvent.Move(1, ExecState.Planned, ExecState.Armed, ExecEventSource.Engine, "system:t", Clock.GetUtcNow())).IsSuccess.ShouldBeTrue();
            Events.Store.Append(IntentId, ExecEvent.Move(2, ExecState.Armed, ExecState.Checking, ExecEventSource.Engine, "system:t", Clock.GetUtcNow())).IsSuccess.ShouldBeTrue();
        }

        public CheckRejectionMover Mover => new(this, this, this, Events, Clock);

        public ExecState State => Events.Store.Live(IntentId)!.State;

        public JsonElement LastPayload => JsonDocument.Parse(Events.Store.Events(IntentId)[^1].Payload).RootElement;

        public void OpenTicket(double expiresInSeconds = 60, params string[] soft) => Ticket = new RejectionTicket(Guid.NewGuid(), null, Clock.GetUtcNow().AddSeconds(expiresInSeconds), soft.Length == 0 ? [CheckCodes.AdvShare] : soft);

        public Task<RejectionView?> ReadAsync(long intentId, CancellationToken cancellationToken)
        {
            BeforeRead?.Invoke();
            return Unreadable ? throw new InvalidOperationException("db down") : Task.FromResult<RejectionView?>(new RejectionView(Account, Run, Ticket, Clock.GetUtcNow()));
        }

        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken)
        {
            ModeReads++;
            return ModeThrows ? throw new InvalidOperationException("mode store down") : Task.FromResult(Mode);
        }

        public Task<bool?> RefuseAsync(Guid ticketId, string reason, string actor, CancellationToken cancellationToken)
        {
            if (RefuseThrows)
            {
                throw new InvalidOperationException("db down");
            }

            OnRefuse?.Invoke();
            OnRefuse = null;
            if (Ticket?.Id != ticketId)
            {
                return Task.FromResult<bool?>(null);
            }

            if (Ticket.Outcome is not null)
            {
                return Task.FromResult<bool?>(false);
            }

            Ticket = Ticket with { Outcome = TicketOutcomes.Refused };
            Refusals.Add((ticketId, reason, actor));
            return Task.FromResult<bool?>(true);
        }

        public Task<TicketIntent?> IntentAsync(long intentId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TicketContext?> ContextAsync(TicketIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<StoredTicket>> TicketsAsync(Guid intentPublicId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> OpenCountAsync(Guid accountId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TicketInsert> InsertAsync(TicketDraft draft, DateTimeOffset issuedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ClosedTicket>> ExpireDueAsync(string actor, CancellationToken cancellationToken)
        {
            OnRefuse?.Invoke();
            OnRefuse = null;
            if (Ticket is not { Outcome: null } t || t.ExpiresAt > Clock.GetUtcNow())
            {
                return Task.FromResult<IReadOnlyList<ClosedTicket>>([]);
            }

            Ticket = t with { Outcome = TicketOutcomes.Expired };
            Expirations.Add((t.Id, actor));
            return Task.FromResult<IReadOnlyList<ClosedTicket>>([new ClosedTicket(t.Id, Guid.Empty, Account, TicketOutcomes.Expired, TicketOutcomes.TtlReason, 90_000)]);
        }
    }

    [Fact]
    public async Task A_soft_only_rejection_with_an_open_ticket_stays_checking()
    {
        var s = new Scene();
        s.OpenTicket();

        var step = await s.Mover.RunAsync(IntentId, Ct);

        step.ShouldBe(RejectionStep.AwaitTicket);
        s.State.ShouldBe(ExecState.Checking);
    }

    [Fact]
    public async Task A_soft_only_rejection_without_a_ticket_moves_checking_to_rejected_with_the_codes()
    {
        var s = new Scene { Run = new RejectionRun(91, CheckVerdict.Rejected, [], [CheckCodes.FnoBan, CheckCodes.AdvShare]) };

        var step = await s.Mover.RunAsync(IntentId, Ct);

        step.ShouldBe(RejectionStep.RejectNow);
        s.State.ShouldBe(ExecState.Rejected);
        s.LastPayload.GetProperty("reason").GetString().ShouldBe(RejectionReasons.NoTicket);
        s.LastPayload.GetProperty("run").GetInt64().ShouldBe(91);
        s.LastPayload.GetProperty("codes").EnumerateArray().Select(c => c.GetString()).ShouldBe([CheckCodes.AdvShare, CheckCodes.FnoBan]);
        s.Events.Store.Events(IntentId)[^1].Actor.ShouldBe(CheckRejectionMover.Actor);
    }

    [Fact]
    public async Task A_hard_failure_with_an_open_ticket_is_rejected_at_once()
    {
        var s = new Scene { Run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.NoHalt], [CheckCodes.AdvShare]) };
        s.OpenTicket();

        var ticket = s.Ticket!.Id;

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.RejectNow);

        s.State.ShouldBe(ExecState.Rejected);
        s.LastPayload.GetProperty("reason").GetString().ShouldBe(RejectionReasons.HardCheck);
        s.Refusals.ShouldBe([(ticket, RejectionReasons.IntentRejected, CheckRejectionMover.Actor)], "the open card is closed before the intent is rejected");
    }

    [Fact]
    public async Task An_unavailable_hard_input_with_an_open_ticket_is_rejected_at_once()
    {
        var s = new Scene { Run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.StreamsAlive], []) };
        s.OpenTicket();

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.RejectNow);

        s.State.ShouldBe(ExecState.Rejected);
    }

    [Theory]
    [InlineData(TicketOutcomes.Expired, RejectionReasons.TicketExpired)]
    [InlineData(TicketOutcomes.Skipped, RejectionReasons.TicketSkipped)]
    [InlineData(TicketOutcomes.Refused, RejectionReasons.TicketRefused)]
    public async Task An_expired_skipped_or_refused_ticket_makes_the_soft_only_rejection_final(string outcome, string reason)
    {
        var s = new Scene();
        s.Ticket = new RejectionTicket(Guid.NewGuid(), outcome, s.Clock.GetUtcNow().AddSeconds(30), [CheckCodes.AdvShare]);

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.RejectNow);

        s.State.ShouldBe(ExecState.Rejected);
        s.LastPayload.GetProperty("reason").GetString().ShouldBe(reason);
    }

    [Fact]
    public async Task A_re_check_racing_the_expiry_rejects_once_the_database_clock_reaches_the_expiry_without_waiting_for_the_expiry_loop()
    {
        var s = new Scene();
        s.OpenTicket(expiresInSeconds: 1);

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.AwaitTicket);
        s.Clock.Advance(TimeSpan.FromSeconds(1));
        var step = await s.Mover.RunAsync(IntentId, Ct);

        step.ShouldBe(RejectionStep.RejectNow);
        s.State.ShouldBe(ExecState.Rejected);
        s.LastPayload.GetProperty("reason").GetString().ShouldBe(RejectionReasons.TicketExpired);
        s.Expirations.ShouldBe([(s.Ticket!.Id, TicketOutcomes.ExpiryActor)], "a card past its expiry is closed expired, never refused: no refusal exists that could unlock a second card");
        s.Refusals.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_confirmed_ticket_for_the_same_soft_set_is_left_to_the_hand_off()
    {
        var s = new Scene();
        s.Ticket = new RejectionTicket(Guid.NewGuid(), TicketOutcomes.Confirmed, s.Clock.GetUtcNow().AddSeconds(30), [CheckCodes.AdvShare]);

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.None);

        s.State.ShouldBe(ExecState.Checking);
    }

    [Fact]
    public async Task A_soft_only_rejection_outside_semi_auto_is_rejected_even_with_an_open_ticket_and_an_unread_mode_moves_nothing()
    {
        var assisted = new Scene { Mode = new ModeAnswer(ExecMode.Assisted, true) };
        assisted.OpenTicket();
        var unread = new Scene { Mode = null };
        unread.OpenTicket();

        (await assisted.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.RejectNow);
        (await unread.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.None);

        assisted.LastPayload.GetProperty("reason").GetString().ShouldBe(RejectionReasons.NotSemiAuto);
        unread.State.ShouldBe(ExecState.Checking);
        unread.Refusals.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unreadable_record_a_run_that_is_not_rejected_and_an_intent_that_is_not_checking_move_nothing()
    {
        var unreadable = new Scene { Unreadable = true };
        var cleared = new Scene { Run = new RejectionRun(91, CheckVerdict.Cleared, [], []) };
        var cancelled = new Scene();
        cancelled.Events.Store.Append(IntentId, ExecEvent.Move(3, ExecState.Checking, ExecState.Cancelled, ExecEventSource.Engine, "system:t", cancelled.Clock.GetUtcNow())).IsSuccess.ShouldBeTrue();

        (await unreadable.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.None);
        (await cleared.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.None);
        (await cancelled.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.None);

        unreadable.State.ShouldBe(ExecState.Checking);
        cleared.State.ShouldBe(ExecState.Checking);
        cancelled.State.ShouldBe(ExecState.Cancelled);
    }

    [Fact]
    public async Task When_the_hand_off_moves_first_the_mover_loses_the_chain_and_stores_nothing()
    {
        var s = new Scene { Run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.FeedFresh], []) };
        s.BeforeRead = () => s.Events.Store.Append(IntentId, ExecEvent.Move(3, ExecState.Checking, ExecState.Submitting, ExecEventSource.Engine, ConfirmHandOff.Actor, s.Clock.GetUtcNow()));

        var step = await s.Mover.RunAsync(IntentId, Ct);

        step.ShouldBe(RejectionStep.None);
        s.State.ShouldBe(ExecState.Submitting);
        s.Events.Store.Events(IntentId).Count.ShouldBe(3);
    }

    [Fact]
    public async Task When_the_mover_rejects_first_a_later_confirmation_is_never_handed_off()
    {
        var s = new Scene();
        s.Ticket = new RejectionTicket(Guid.NewGuid(), TicketOutcomes.Skipped, s.Clock.GetUtcNow().AddSeconds(30), [CheckCodes.AdvShare]);
        await s.Mover.RunAsync(IntentId, Ct);
        var open = new StubGate();

        var handed = await new ConfirmHandOff(open, s.Events, s.Clock).RunAsync(IntentId, Ct);

        handed.ShouldBe(HandOffResult.Wait);
        s.State.ShouldBe(ExecState.Rejected);
    }

    [Fact]
    public async Task When_the_owner_confirms_an_unexpired_card_between_the_read_and_the_close_a_hard_failure_still_rejects_and_nothing_is_refused()
    {
        var s = new Scene { Run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.NoHalt], [CheckCodes.AdvShare]) };
        s.OpenTicket();
        s.OnRefuse = () => s.Ticket = s.Ticket! with { Outcome = TicketOutcomes.Confirmed };

        var step = await s.Mover.RunAsync(IntentId, Ct);

        step.ShouldBe(RejectionStep.RejectNow);
        s.State.ShouldBe(ExecState.Rejected);
        s.LastPayload.GetProperty("reason").GetString().ShouldBe(RejectionReasons.HardCheck);
        s.Refusals.ShouldBeEmpty();
        s.Ticket!.Outcome.ShouldBe(TicketOutcomes.Confirmed, "the confirmation stays as stored; the hand-off moves only a checking intent");
    }

    [Fact]
    public async Task When_the_expiry_loop_closes_the_card_between_the_read_and_the_close_the_intent_is_rejected_for_the_expiry()
    {
        var s = new Scene();
        s.OpenTicket(expiresInSeconds: 0);
        s.OnRefuse = () => s.Ticket = s.Ticket! with { Outcome = TicketOutcomes.Expired };

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.RejectNow);

        s.LastPayload.GetProperty("reason").GetString().ShouldBe(RejectionReasons.TicketExpired);
        s.Refusals.ShouldBeEmpty();
        s.Expirations.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_move_that_fails_after_the_card_was_refused_leaves_the_intent_checking_with_a_closed_card_that_has_no_successor()
    {
        var s = new Scene { Run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.NoHalt], []) };
        s.OpenTicket();
        s.Events.AppendThrows = true;

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.None);

        s.State.ShouldBe(ExecState.Checking);
        s.Refusals.Single().Reason.ShouldBe(RejectionReasons.IntentRejected, "the issuer treats this reason as final (IssueConfirmTicketTests)");
        s.Events.AppendThrows = false;
        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.RejectNow, "the next pass finishes the move");
        s.State.ShouldBe(ExecState.Rejected);
    }

    [Fact]
    public async Task A_hard_failure_is_rejected_without_reading_the_mode_so_a_failing_mode_source_cannot_delay_it()
    {
        var s = new Scene { Run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.FeedFresh], []), ModeThrows = true };
        s.OpenTicket();

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.RejectNow);

        s.State.ShouldBe(ExecState.Rejected);
        s.ModeReads.ShouldBe(0);
    }

    [Fact]
    public async Task A_soft_only_rejection_with_a_throwing_mode_source_moves_nothing()
    {
        var s = new Scene { ModeThrows = true };
        s.OpenTicket(expiresInSeconds: 0);

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.None);

        s.State.ShouldBe(ExecState.Checking);
        s.Ticket!.Outcome.ShouldBeNull();
    }

    [Fact]
    public async Task A_refusal_that_fails_leaves_the_intent_checking_and_never_rejected_with_an_open_card()
    {
        var s = new Scene { Run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.NoHalt], []), RefuseThrows = true };
        s.OpenTicket();

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.None);

        s.State.ShouldBe(ExecState.Checking);
        s.Ticket!.Outcome.ShouldBeNull();
    }

    [Fact]
    public async Task A_second_ticket_issued_between_the_refusal_and_the_move_is_closed_after_the_move()
    {
        var s = new Scene { Run = new RejectionRun(91, CheckVerdict.Rejected, [CheckCodes.NoHalt], []) };
        s.OpenTicket();
        var second = Guid.NewGuid();
        var first = s.Ticket!.Id;
        s.OnRefuse = () => s.BeforeRead = () =>
        {
            if (s.Ticket is { Outcome: not null } && s.Ticket.Id == first)
            {
                s.Ticket = new RejectionTicket(second, null, s.Clock.GetUtcNow().AddSeconds(90), [CheckCodes.AdvShare]);
            }
        };

        (await s.Mover.RunAsync(IntentId, Ct)).ShouldBe(RejectionStep.RejectNow);

        s.State.ShouldBe(ExecState.Rejected);
        s.Refusals.Select(r => r.Ticket).ShouldBe([first, second]);
    }

    private sealed class StubGate : IConfirmGate
    {
        public Task<GateDecision> CheckAsync(long intentId, CancellationToken cancellationToken) => Task.FromResult(new GateDecision(true, TicketOutcomes.Confirmed));
    }
}
