using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Execution;

/// <summary>
/// Spec P12-08 criteria 3 and 5 to 11 at the use-case level, on fakes: a tap that holds stores one confirmed outcome; a repeat answers the saved decision; a changed text, a late tap and each state refusal store nothing; a price or Soft-set
/// refusal stores nothing and asks the execution service to close the ticket; the password and the Soft reason are asked when the ticket says so; a skip is a decision; the day unlock is for a confirmable account on a trading day only.
/// </summary>
public sealed class ConfirmTicketHandlerTests
{
    private const string Owner = "owner:hem@example.invalid";
    private const string Password = "right-password";

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);
    private static readonly DateOnly Session = new(2026, 10, 27);
    private static readonly DateTimeOffset Issued = new(2026, 10, 27, 9, 46, 0, Ist);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Access : IConfirmTicketAccess
    {
        public ConfirmTicketRow Ticket { get; set; } = Row();

        public ConfirmFacts Facts { get; set; } = Good();

        public List<(Guid Ticket, string Outcome, string Reason, string Actor, string? Note)> Outcomes { get; } = [];

        public OutcomeWrite? Next { get; set; }

        public static ConfirmTicketRow Row(bool password = false, IReadOnlyList<string>? soft = null) => new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.NewGuid(), "Main", Guid.NewGuid(), 501, Session, "entry", "BUY 120 TATAPOWER ...", new string('a', 64), "XD261027000001E1", 412.30m, 49_482m, 870m, password,
            password ? [PasswordRule.NotionalOverStepUp] : [], soft ?? [], Issued, Issued.AddSeconds(90), null, null, null);

        public static ConfirmFacts Good() =>
            new(new ConfirmState(Issued.AddSeconds(30), Issued.AddSeconds(90), Session, true, "semi_auto", false, "ready", Session, Issued.AddMinutes(-3), true), 412.40m, Issued.AddSeconds(29), [], ConfirmSettings.Default);

        public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => Task.FromResult(Facts.State.Now);

        public Task<IReadOnlyList<ConfirmTicketRow>> ListAsync(DateOnly session, bool openOnly, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConfirmTicketRow>>(Ticket.SessionDate == session && (!openOnly || Ticket.Outcome is null) ? [Ticket] : []);

        public Task<ConfirmTicketRow?> FindAsync(Guid ticketId, CancellationToken cancellationToken) => Task.FromResult(ticketId == Ticket.Id ? Ticket : null);

        public Task<ConfirmFacts> FactsAsync(ConfirmTicketRow ticket, CancellationToken cancellationToken) => Task.FromResult(Facts);

        public Task<OutcomeWrite> InsertOutcomeAsync(Guid ticketId, string outcome, string reason, string actor, string? note, CancellationToken cancellationToken)
        {
            if (Next is { } next)
            {
                return Task.FromResult(next);
            }

            if (Ticket.Outcome is not null)
            {
                return Task.FromResult(new OutcomeWrite(null, true, null));
            }

            var at = Facts.State.Now.AddMilliseconds(5);
            Outcomes.Add((ticketId, outcome, reason, actor, note));
            Ticket = Ticket with { Outcome = outcome, OutcomeReason = reason, DecidedAt = at };
            return Task.FromResult(new OutcomeWrite(at, false, null));
        }
    }

    private sealed class Commands : IExecutionCommands
    {
        public List<(string Kind, Guid? Account, string Payload, string Actor)> Rows { get; } = [];

        public Task<Guid> EnqueueAsync(string kind, Guid? accountId, string payloadJson, string actor, CancellationToken cancellationToken)
        {
            Rows.Add((kind, accountId, payloadJson, actor));
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class StepUp : ICommandHandler<StepUpCommand, StepUpResult>
    {
        public int Calls { get; private set; }

        public ValueTask<Result<StepUpResult>> HandleAsync(StepUpCommand command, CancellationToken cancellationToken)
        {
            Calls++;
            command.Elevate.ShouldBeFalse();
            return ValueTask.FromResult(command.Password == Password ? Result.Success(new StepUpResult(DateTimeOffset.UnixEpoch)) : Result.Failure<StepUpResult>(AuthErrors.InvalidCredentials));
        }
    }

    private sealed record Scene(Access Access, Commands Commands, StepUp StepUp)
    {
        public ConfirmTicketHandler Handler => new(Access, Commands, StepUp, NullLogger<ConfirmTicketHandler>.Instance);

        public SkipTicketHandler Skip => new(Access, NullLogger<SkipTicketHandler>.Instance);
    }

    private static Scene New() => new(new Access(), new Commands(), new StepUp());

    private static ConfirmTicketCommand Tap(Scene s, string? hash = null, string? password = null, string? reason = null, string actor = Owner) =>
        new(actor, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), s.Access.Ticket.Id, hash ?? s.Access.Ticket.TextHash, password, reason);

    [Fact]
    public async Task A_tap_that_holds_stores_one_confirmed_outcome_by_the_owner_and_answers_not_already_decided()
    {
        var s = New();

        var result = await s.Handler.HandleAsync(Tap(s), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new ConfirmResultView(s.Access.Ticket.Id, TicketOutcomes.Confirmed, false, s.Access.Ticket.DecidedAt!.Value));
        s.Access.Outcomes.ShouldBe([(s.Access.Ticket.Id, TicketOutcomes.Confirmed, TicketOutcomes.TapReason, Owner, null)]);
        s.Commands.Rows.ShouldBeEmpty();
        s.StepUp.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_repeat_answers_the_saved_decision_with_the_same_time_and_a_tap_after_a_skip_is_ticket_closed()
    {
        var s = New();
        var first = await s.Handler.HandleAsync(Tap(s), Ct);

        var again = await s.Handler.HandleAsync(Tap(s), Ct);

        again.Value.ShouldBe(first.Value with { AlreadyDecided = true });
        s.Access.Outcomes.Count.ShouldBe(1);

        var skipped = New();
        (await skipped.Skip.HandleAsync(new SkipTicketCommand(Owner, skipped.Access.Ticket.Id, "not today"), Ct)).IsSuccess.ShouldBeTrue();
        var closed = await skipped.Handler.HandleAsync(Tap(skipped), Ct);
        closed.Error.Code.ShouldBe(ConfirmCodes.TicketClosed);
        closed.Error.Message.ShouldContain("skipped");
        closed.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task A_lost_race_on_the_insert_reads_the_winner_back_as_already_decided()
    {
        var s = New();
        s.Access.Next = new OutcomeWrite(null, true, null);
        var at = Issued.AddSeconds(31);
        var winner = s.Access.Ticket with { Outcome = TicketOutcomes.Confirmed, OutcomeReason = TicketOutcomes.TapReason, DecidedAt = at };
        var handler = new ConfirmTicketHandler(new RaceAccess(s.Access, winner), s.Commands, s.StepUp, NullLogger<ConfirmTicketHandler>.Instance);

        var result = await handler.HandleAsync(Tap(s), Ct);

        result.Value.ShouldBe(new ConfirmResultView(winner.Id, TicketOutcomes.Confirmed, true, at));
    }

    [Fact]
    public async Task A_text_hash_that_is_not_the_stored_one_is_text_changed_and_nothing_is_stored()
    {
        var s = New();

        var result = await s.Handler.HandleAsync(Tap(s, hash: new string('b', 64)), Ct);

        result.Error.Code.ShouldBe(ConfirmCodes.TextChanged);
        s.Access.Outcomes.ShouldBeEmpty();
        s.Commands.Rows.ShouldBeEmpty();
    }

    public static TheoryData<string, string> StateCases => new()
    {
        { "late", ConfirmCodes.TicketExpired },
        { "disabled", ConfirmCodes.Disabled },
        { "mode", ConfirmCodes.ModeChanged },
        { "halted", ConfirmCodes.Halted },
        { "not_ready", ConfirmCodes.NotReady },
        { "locked", ConfirmCodes.NotUnlocked },
    };

    [Theory]
    [MemberData(nameof(StateCases))]
    public async Task Each_state_refusal_answers_its_own_code_stores_nothing_and_leaves_the_ticket_open(string change, string code)
    {
        var s = New();
        var state = s.Access.Facts.State;
        s.Access.Facts = s.Access.Facts with
        {
            State = change switch
            {
                "late" => state with { Now = state.ExpiresAt },
                "disabled" => state with { ExecutionEnabled = false },
                "mode" => state with { EffectiveMode = "assisted" },
                "halted" => state with { Halted = true },
                "not_ready" => state with { ReadinessVerdict = "unavailable" },
                _ => state with { Unlocked = false },
            },
        };

        var result = await s.Handler.HandleAsync(Tap(s, password: Password), Ct);

        result.Error.Code.ShouldBe(code);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Message.ShouldContain("Nothing was sent");
        s.Access.Outcomes.ShouldBeEmpty();
        s.Commands.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(413.54, ConfirmCodes.PriceMoved)]
    [InlineData(411.06, ConfirmCodes.PriceMoved)]
    [InlineData(null, ConfirmCodes.PriceUnavailable)]
    public async Task A_price_refusal_stores_nothing_and_asks_the_execution_service_to_close_the_ticket(double? last, string code)
    {
        var s = New();
        s.Access.Facts = s.Access.Facts with { LastPrice = (decimal?)last };

        var result = await s.Handler.HandleAsync(Tap(s), Ct);

        result.Error.Code.ShouldBe(code);
        s.Access.Outcomes.ShouldBeEmpty();
        var row = s.Commands.Rows.ShouldHaveSingleItem();
        (row.Kind, row.Account, row.Actor).ShouldBe(("ticket_refuse", s.Access.Ticket.AccountId, Owner));
        row.Payload.ShouldContain(s.Access.Ticket.Id.ToString());
        row.Payload.ShouldContain(code);
    }

    [Fact]
    public async Task A_413_53_tap_passes_the_drift_limit()
    {
        var s = New();
        s.Access.Facts = s.Access.Facts with { LastPrice = 413.53m };

        (await s.Handler.HandleAsync(Tap(s), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_changed_soft_set_is_soft_set_changed_and_closes_the_ticket_and_the_same_set_needs_a_reason_that_is_stored()
    {
        var s = New();
        s.Access.Ticket = Access.Row(password: true, soft: ["adv_share"]);
        s.Access.Facts = s.Access.Facts with { SoftNow = ["adv_share", "fno_ban"] };

        (await s.Handler.HandleAsync(Tap(s, password: Password, reason: "known"), Ct)).Error.Code.ShouldBe(ConfirmCodes.SoftSetChanged);
        s.Commands.Rows.ShouldHaveSingleItem().Payload.ShouldContain(ConfirmCodes.SoftSetChanged);

        var same = New();
        same.Access.Ticket = Access.Row(password: true, soft: ["adv_share"]);
        same.Access.Facts = same.Access.Facts with { SoftNow = ["adv_share"] };
        (await same.Handler.HandleAsync(Tap(same, password: Password), Ct)).Error.Code.ShouldBe(ConfirmCodes.ReasonRequired);
        (await same.Handler.HandleAsync(Tap(same, password: Password, reason: "  low volume known  "), Ct)).IsSuccess.ShouldBeTrue();
        same.Access.Outcomes.ShouldHaveSingleItem().Note.ShouldBe("low volume known");
    }

    [Fact]
    public async Task A_ticket_that_asks_for_the_password_is_400_without_it_401_with_a_wrong_one_and_confirmed_with_the_right_one()
    {
        var s = New();
        s.Access.Ticket = Access.Row(password: true);

        var missing = await s.Handler.HandleAsync(Tap(s), Ct);
        var wrong = await s.Handler.HandleAsync(Tap(s, password: "nope"), Ct);
        var right = await s.Handler.HandleAsync(Tap(s, password: Password), Ct);

        (missing.Error.Code, missing.Error.Kind).ShouldBe((ConfirmCodes.PasswordRequired, ErrorKind.Validation));
        (wrong.Error.Code, wrong.Error.Kind).ShouldBe((AuthErrors.InvalidCredentialsCode, ErrorKind.Unauthorized));
        right.IsSuccess.ShouldBeTrue();
        s.StepUp.Calls.ShouldBe(2);
        s.Access.Outcomes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_database_refusal_comes_back_as_its_code_and_nothing_else_is_written()
    {
        var s = New();
        s.Access.Next = new OutcomeWrite(null, false, ConfirmCodes.TicketExpired);

        var result = await s.Handler.HandleAsync(Tap(s), Ct);

        result.Error.Code.ShouldBe(ConfirmCodes.TicketExpired);
        s.Commands.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_owner_taps_and_an_unknown_ticket_is_not_found()
    {
        var s = New();

        (await s.Handler.HandleAsync(Tap(s, actor: "system:x"), Ct)).Error.Kind.ShouldBe(ErrorKind.Forbidden);
        (await s.Handler.HandleAsync(Tap(s) with { TicketId = Guid.NewGuid() }, Ct)).Error.Code.ShouldBe(ConfirmCodes.NotFound);
        s.Access.Outcomes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_skip_is_a_declined_decision_with_an_optional_plain_reason_and_a_repeat_answers_it()
    {
        var s = New();

        (await s.Skip.HandleAsync(new SkipTicketCommand(Owner, s.Access.Ticket.Id, new string('x', 201)), Ct)).Error.Code.ShouldBe(ConfirmCodes.ValuesInvalid);
        var first = await s.Skip.HandleAsync(new SkipTicketCommand(Owner, s.Access.Ticket.Id, " busy "), Ct);
        var again = await s.Skip.HandleAsync(new SkipTicketCommand(Owner, s.Access.Ticket.Id, null), Ct);

        first.Value.Outcome.ShouldBe(TicketOutcomes.Skipped);
        again.Value.ShouldBe(first.Value with { AlreadyDecided = true });
        s.Access.Outcomes.ShouldBe([(s.Access.Ticket.Id, TicketOutcomes.Skipped, TicketOutcomes.DeclinedReason, Owner, "busy")]);
        (await s.Handler.HandleAsync(Tap(s), Ct)).Error.Code.ShouldBe(ConfirmCodes.TicketClosed);
    }

    [Fact]
    public async Task The_list_shows_server_now_and_a_ticket_past_its_expiry_reads_expired_before_the_loop_writes_it()
    {
        var s = New();
        var handler = new GetConfirmTicketsHandler(s.Access);

        var open = (await handler.HandleAsync(new GetConfirmTicketsQuery(null, Session), Ct)).Value.ShouldHaveSingleItem();
        open.State.ShouldBe(TicketOutcomes.Open);
        open.ServerNow.ShouldBe(s.Access.Facts.State.Now);

        s.Access.Facts = s.Access.Facts with { State = s.Access.Facts.State with { Now = Issued.AddSeconds(91) } };
        (await handler.HandleAsync(new GetConfirmTicketsQuery("open", Session), Ct)).Value.ShouldBeEmpty();
        var all = (await handler.HandleAsync(new GetConfirmTicketsQuery("all", Session), Ct)).Value.ShouldHaveSingleItem();
        (all.State, all.OutcomeReason).ShouldBe((TicketOutcomes.Expired, TicketOutcomes.TtlReason));
        (await handler.HandleAsync(new GetConfirmTicketsQuery("closed", Session), Ct)).Error.Code.ShouldBe(ConfirmCodes.ValuesInvalid);
    }

    private sealed class Unlocks(string? mode, DateTimeOffset now, string? refused = null) : IDayUnlockAccess
    {
        public List<(Guid Account, DateOnly Session, string Actor)> Rows { get; } = [];

        public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => Task.FromResult(now);

        public Task<IReadOnlyList<DayUnlockView>> ListAsync(DateOnly session, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DayUnlockView>>([]);

        public Task<string?> EffectiveModeAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(mode);

        public Task<(DayUnlockView? Row, string? Refused)> InsertAsync(Guid accountId, DateOnly session, string actor, CancellationToken cancellationToken)
        {
            if (refused is not null)
            {
                return Task.FromResult<(DayUnlockView?, string?)>((null, refused));
            }

            Rows.Add((accountId, session, actor));
            return Task.FromResult<(DayUnlockView?, string?)>((new DayUnlockView(1, accountId, session, now, DayUnlock.ExpiresAt(session)), null));
        }
    }

    private sealed class Calendar : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

        public void Invalidate()
        {
        }
    }

    private static UnlockDayHandler Unlock(Unlocks access) => new(access, new Calendar(), NullLogger<UnlockDayHandler>.Instance);

    [Fact]
    public async Task A_day_unlock_is_stored_for_today_by_the_database_date_for_a_semi_auto_account_on_a_trading_day()
    {
        var account = Guid.NewGuid();
        var access = new Unlocks("semi_auto", new DateTimeOffset(2026, 10, 27, 9, 5, 0, Ist));

        var result = await Unlock(access).HandleAsync(new UnlockDayCommand(Owner, account), Ct);

        result.Value.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 10, 27, 15, 30, 0, Ist));
        access.Rows.ShouldBe([(account, Session, Owner)]);
    }

    [Theory]
    [InlineData("assisted", 27, null, ConfirmCodes.ModeNotExecuting)]
    [InlineData("paper", 27, null, ConfirmCodes.ModeNotExecuting)]
    [InlineData(null, 27, null, ConfirmCodes.NotFound)]
    [InlineData("auto_live", 31, null, ConfirmCodes.NotTradingDay)]
    [InlineData("semi_auto", 27, ConfirmCodes.UnlockClosed, ConfirmCodes.UnlockClosed)]
    public async Task A_day_unlock_is_refused_for_another_mode_an_unknown_account_a_weekend_or_after_the_close(string? mode, int day, string? refused, string code)
    {
        var access = new Unlocks(mode, new DateTimeOffset(2026, 10, day, 9, 5, 0, Ist), refused);

        var result = await Unlock(access).HandleAsync(new UnlockDayCommand(Owner, Guid.NewGuid()), Ct);

        result.Error.Code.ShouldBe(code);
        access.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_owner_unlocks_and_an_unlock_names_an_account()
    {
        var access = new Unlocks("semi_auto", Issued);

        (await Unlock(access).HandleAsync(new UnlockDayCommand("system:x", Guid.NewGuid()), Ct)).Error.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Unlock(access).HandleAsync(new UnlockDayCommand(Owner, null), Ct)).Error.Code.ShouldBe(ConfirmCodes.ValuesInvalid);
    }

    /// <summary>Answers the insert as a lost race and the read-back as the winner's row.</summary>
    private sealed class RaceAccess(Access inner, ConfirmTicketRow winner) : IConfirmTicketAccess
    {
        private int _finds;

        public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => inner.NowAsync(cancellationToken);

        public Task<IReadOnlyList<ConfirmTicketRow>> ListAsync(DateOnly session, bool openOnly, CancellationToken cancellationToken) => inner.ListAsync(session, openOnly, cancellationToken);

        public Task<ConfirmTicketRow?> FindAsync(Guid ticketId, CancellationToken cancellationToken) => Task.FromResult<ConfirmTicketRow?>(++_finds == 1 ? inner.Ticket : winner);

        public Task<ConfirmFacts> FactsAsync(ConfirmTicketRow ticket, CancellationToken cancellationToken) => inner.FactsAsync(ticket, cancellationToken);

        public Task<OutcomeWrite> InsertOutcomeAsync(Guid ticketId, string outcome, string reason, string actor, string? note, CancellationToken cancellationToken) => Task.FromResult(new OutcomeWrite(null, true, null));
    }
}
