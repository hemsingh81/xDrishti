#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Auth;
using XDrishti.Application.Selection;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Audit;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Selection;

/// <summary>
/// P15-06 criteria 8 and 9: the owner's switch (owner only, a reason, a fresh password for every change ON and OFF, one row as the owner with the settings snapshot, an account event of the category selection), the effective value from the newest row (no row, or an unreadable history, is OFF),
/// the view with the reasons the filter is not acting, and the auto option: only a KEEP verdict, recorded as auto and never as an owner decision, inert while the verdict port has no implementation.
/// </summary>
public sealed class EventFilterSwitchTests
{
    private const string Owner = "owner:hem@example.invalid";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeDecisions : IEventFilterDecisions
    {
        public List<EventFilterRow> Rows { get; } = [];

        public bool Unreadable { get; set; }

        public Exception? ThrowOnAppend { get; set; }

        public string? Refuse { get; set; }

        public Task<IReadOnlyList<EventFilterRow>> RowsAsync(int take, CancellationToken cancellationToken) =>
            Unreadable ? throw new InvalidOperationException("the table is away") : Task.FromResult<IReadOnlyList<EventFilterRow>>([.. Rows.OrderByDescending(r => r.Id).Take(take)]);

        public Task<(long? Id, string? Refused)> AppendAsync(NewEventFilterRow row, CancellationToken cancellationToken)
        {
            if (ThrowOnAppend is not null)
            {
                throw ThrowOnAppend;
            }

            if (Refuse is not null)
            {
                return Task.FromResult<(long?, string?)>((null, Refuse));
            }

            var id = Rows.Count + 1L;
            Rows.Add(new EventFilterRow(id, row.Enabled, row.ResultsWithinDays, row.Approval, row.DecidedBy, row.Actor, row.Reason, row.SettingSnapshot, row.VerdictRef, new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero).AddMinutes(id)));
            return Task.FromResult<(long?, string?)>((id, null));
        }
    }

    private sealed class Events : IAccountEventLog
    {
        public List<AccountEvent> Recorded { get; } = [];

        public void Record(AccountEvent accountEvent) => Recorded.Add(accountEvent);

        public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<AccountEvent>>([]);
    }

    private sealed class Unit(bool fail = false) : IAccountUnitOfWork
    {
        public ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken) =>
            fail ? throw new InvalidOperationException("audit store away") : ValueTask.FromResult(true);

        public ValueTask<Result> RunSerializedAsync(Func<CancellationToken, ValueTask<Result>> work, CancellationToken cancellationToken) => work(cancellationToken);
    }

    private sealed class Verdict(EventFilterVerdict? answer, bool throws = false) : IEventFilterVerdict
    {
        public ValueTask<EventFilterVerdict?> NewestAsync(string ruleVersion, CancellationToken cancellationToken) =>
            throws ? throw new InvalidOperationException("verdicts away") : ValueTask.FromResult(answer);
    }

    private sealed record Rig(FakeDecisions Decisions, Events Events, ICommandHandler<StepUpCommand, StepUpResult> Password, ChangeEventFilterHandler Handler);

    private static readonly ClientInfo Client = new("127.0.0.1", "test");

    private static Rig Build(bool passwordOk = true, bool auditFails = false)
    {
        var decisions = new FakeDecisions();
        var events = new Events();
        var password = Substitute.For<ICommandHandler<StepUpCommand, StepUpResult>>();
        password.HandleAsync(Arg.Any<StepUpCommand>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(passwordOk ? Result.Success(new StepUpResult(DateTimeOffset.UnixEpoch)) : Result.Failure<StepUpResult>(AuthErrors.InvalidCredentials)));
        var view = new GetEventFilterHandler(decisions, Substitute.For<IResultsCalendar>(), Substitute.For<IFilingInstruments>(), new Verdict(null), TimeProvider.System);
        return new Rig(decisions, events, password, new ChangeEventFilterHandler(decisions, password, view, events, new Unit(auditFails), TimeProvider.System, NullLogger<ChangeEventFilterHandler>.Instance));
    }

    private static ChangeEventFilterCommand Command(bool? enabled = true, int? days = null, string? approval = null, string? reason = "turn the results filter on for the paper epoch", string? password = "pw", string actor = Owner) =>
        new(actor, Guid.NewGuid(), Client, enabled, days, approval, reason, password);

    [Fact]
    public async Task The_owner_switches_it_on_with_a_fresh_password_and_the_row_is_the_owners_with_the_snapshot_and_no_verdict()
    {
        var rig = Build();

        var result = await rig.Handler.HandleAsync(Command(days: 3), Ct);

        result.IsSuccess.ShouldBeTrue();
        var row = rig.Decisions.Rows.ShouldHaveSingleItem();
        (row.Enabled, row.ResultsWithinDays, row.Approval, row.DecidedBy, row.Actor, row.VerdictRef).ShouldBe((true, 3, "manual", "owner", Owner, null));
        row.SettingSnapshot.ShouldBe("{\"smart_rules.event_filter.approval\":\"manual\",\"smart_rules.event_filter.enabled\":true,\"smart_rules.event_filter.results_within_days\":3}");
        result.Value.Enabled.ShouldBeTrue();
        await rig.Password.Received(1).HandleAsync(Arg.Is<StepUpCommand>(c => c.Password == "pw" && !c.Elevate), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Switching_off_needs_the_password_too()
    {
        var rig = Build();
        await rig.Handler.HandleAsync(Command(), Ct);

        var off = await rig.Handler.HandleAsync(Command(enabled: false), Ct);

        off.Value.Enabled.ShouldBeFalse();
        rig.Decisions.Rows.Count.ShouldBe(2);
        await rig.Password.Received(2).HandleAsync(Arg.Is<StepUpCommand>(c => !c.Elevate), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_wrong_password_stores_nothing_and_says_so_with_the_login_problem()
    {
        var rig = Build(passwordOk: false);

        var result = await rig.Handler.HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe(AuthErrors.InvalidCredentials.Code);
        rig.Decisions.Rows.ShouldBeEmpty();
        rig.Events.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("service:feed")]
    [InlineData("owner:")]
    [InlineData("auto:event-filter")]
    [InlineData("")]
    public async Task Only_the_signed_in_owner_may_change_it_and_the_auto_actor_is_not_an_owner(string actor)
    {
        var rig = Build();

        var result = await rig.Handler.HandleAsync(Command(actor: actor), Ct);

        result.Error.Code.ShouldBe("selection.owner_only");
        rig.Decisions.Rows.ShouldBeEmpty();
        await rig.Password.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("the password is hunter2")]
    public async Task A_missing_or_unsafe_reason_is_refused_before_the_password_is_checked(string? reason)
    {
        var rig = Build();

        var result = await rig.Handler.HandleAsync(Command(reason: reason), Ct);

        result.Error.Code.ShouldBe("selection.reason_invalid");
        await rig.Password.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);
        (await rig.Handler.HandleAsync(Command(reason: new string('r', 301)), Ct)).Error.Code.ShouldBe("selection.reason_invalid");
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(11, null)]
    [InlineData(null, "yes")]
    [InlineData(null, "Auto")]
    public async Task A_window_outside_0_to_10_or_an_unknown_approval_is_invalid_setting(int? days, string? approval)
    {
        var rig = Build();

        (await rig.Handler.HandleAsync(Command(days: days, approval: approval), Ct)).Error.Code.ShouldBe("selection.invalid_setting");
        (await rig.Handler.HandleAsync(Command(enabled: null), Ct)).Error.Code.ShouldBe("selection.invalid_setting");
        rig.Decisions.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_window_ends_0_and_10_are_valid_and_values_left_out_keep_what_is_in_force()
    {
        var rig = Build();
        await rig.Handler.HandleAsync(Command(days: 0, approval: "auto"), Ct);
        await rig.Handler.HandleAsync(Command(days: 10), Ct);

        var third = await rig.Handler.HandleAsync(Command(enabled: false), Ct);

        (third.Value.ResultsWithinDays, third.Value.Approval).ShouldBe((10, "auto"));
        rig.Decisions.Rows.Select(r => (r.ResultsWithinDays, r.Approval)).ShouldBe([(0, "auto"), (10, "auto"), (10, "auto")]);
    }

    [Fact]
    public async Task Setting_the_approval_to_auto_is_an_owner_row_with_a_password_and_never_an_auto_row()
    {
        var rig = Build();

        await rig.Handler.HandleAsync(Command(enabled: false, approval: "auto"), Ct);

        var row = rig.Decisions.Rows.ShouldHaveSingleItem();
        (row.Approval, row.DecidedBy, row.Actor, row.Enabled).ShouldBe(("auto", "owner", Owner, false));
    }

    [Fact]
    public async Task A_change_leaves_one_account_event_of_the_selection_category_without_the_reason_or_the_password()
    {
        var rig = Build();

        await rig.Handler.HandleAsync(Command(reason: "plain reason text"), Ct);

        var recorded = rig.Events.Recorded.ShouldHaveSingleItem();
        recorded.Event.ShouldBe(AccountEventType.EventFilterSwitched);
        recorded.Actor.ShouldBe(Owner);
        recorded.Detail.ShouldBe("decision=1 enabled=on results_within_days=2 approval=manual decided_by=owner");
        AuditKinds.CategoryOf(AccountEventType.EventFilterSwitched).ShouldBe("selection");
        AuditKinds.Categories.ShouldContain("selection");
    }

    [Fact]
    public async Task A_failed_audit_write_leaves_the_row_standing_and_a_database_refusal_is_a_conflict()
    {
        var failing = Build(auditFails: true);
        var refusing = Build();
        refusing.Decisions.Refuse = "an automatic row needs approval auto in force";

        (await failing.Handler.HandleAsync(Command(), Ct)).IsSuccess.ShouldBeTrue();
        var refused = await refusing.Handler.HandleAsync(Command(), Ct);

        failing.Decisions.Rows.ShouldHaveSingleItem();
        refused.Error.Code.ShouldBe("selection.stale");
        refusing.Decisions.Rows.ShouldBeEmpty();
        refusing.Events.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_effective_value_is_the_newest_row_no_row_is_off_and_an_unreadable_history_is_off()
    {
        var decisions = new FakeDecisions();
        var sw = new EventFilterSwitch(decisions);

        (await sw.CurrentAsync(Ct)).ShouldBe(EventFilterState.Off);
        decisions.Rows.Add(new EventFilterRow(1, true, 4, "manual", "owner", Owner, "r", "{}", null, DateTimeOffset.UnixEpoch));
        decisions.Rows.Add(new EventFilterRow(2, false, 4, "manual", "owner", Owner, "r", "{}", null, DateTimeOffset.UnixEpoch));
        (await sw.CurrentAsync(Ct)).Enabled.ShouldBeFalse();
        decisions.Rows.Add(new EventFilterRow(3, true, 5, "auto", "owner", Owner, "r", "{}", null, DateTimeOffset.UnixEpoch));
        (await sw.CurrentAsync(Ct)).ShouldBe(new EventFilterState(true, 5, "auto", "owner"));
        decisions.Unreadable = true;
        (await sw.CurrentAsync(Ct)).ShouldBe(EventFilterState.Off, "an unreadable table is OFF");
    }

    [Fact]
    public async Task An_owner_off_row_directly_after_an_on_row_is_an_owner_switch_off_and_an_arming_row_is_not()
    {
        var decisions = new FakeDecisions();
        var sw = new EventFilterSwitch(decisions);
        decisions.Rows.Add(OwnerRow(1, false, "auto"));
        (await sw.CurrentAsync(Ct)).OwnerSwitchedOff.ShouldBeFalse("off after nothing: the owner only armed the auto option");
        decisions.Rows.Add(OwnerRow(2, true, "auto"));
        decisions.Rows.Add(OwnerRow(3, false, "auto"));
        (await sw.CurrentAsync(Ct)).OwnerSwitchedOff.ShouldBeTrue();
        decisions.Rows.Add(new EventFilterRow(4, true, 2, "auto", "owner", Owner, "r", "{}", null, DateTimeOffset.UnixEpoch));
        (await sw.CurrentAsync(Ct)).OwnerSwitchedOff.ShouldBeFalse("the owner turned it on again");
    }

    [Fact]
    public async Task After_an_owner_switch_off_a_keep_verdict_changes_nothing_while_approval_stays_auto()
    {
        var (handler, decisions, events) = Auto(new EventFilterVerdict("KEEP", "evf-1", "pf-1"));
        decisions.Rows.Add(OwnerRow(1, true, "auto"));
        decisions.Rows.Add(OwnerRow(2, false, "auto"));

        (await handler.HandleAsync(new ApplyEventFilterAutoCommand(), Ct)).Value.ShouldBe(new EventFilterAutoResult("none", "owner_off"));

        decisions.Rows.Count.ShouldBe(2);
        events.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_view_lists_the_reasons_the_filter_is_not_acting_and_the_coverage()
    {
        var decisions = new FakeDecisions();
        var calendar = Substitute.For<IResultsCalendar>();
        var instruments = Substitute.For<IFilingInstruments>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        instruments.TrackedAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<TrackedInstrument>>([new(a, "AAA"), new(b, "BBB")]));
        var c = Guid.NewGuid();
        instruments.TrackedAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<TrackedInstrument>>([new(a, "AAA"), new(b, "BBB"), new(c, "CCC")]));
        calendar.DaysToResults(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyDictionary<Guid, ResultsAnswer>>(new Dictionary<Guid, ResultsAnswer>
        {
            [a] = new ResultsAnswer(ResultsAnswer.Value, 6, null, null, null, null),
            [b] = ResultsAnswer.Missing(ResultsAnswer.NoEntry),
            [c] = ResultsAnswer.Missing(ResultsAnswer.NoUpcomingEntry),
        }));
        var handler = new GetEventFilterHandler(decisions, calendar, instruments, new Verdict(null), TimeProvider.System);

        var view = await handler.HandleAsync(new GetEventFilterQuery(), Ct);

        view.Enabled.ShouldBeFalse();
        view.NotActingBecause.Select(r => (r.Code, r.Count)).ShouldBe([("switch_off", (int?)null), ("no_entry_for_instruments", 1), ("no_upcoming_entry_for_instruments", 1)]);
        (view.InstrumentsTracked, view.InstrumentsWithEntry, view.AutoVerdict).ShouldBe((3, 1, "unavailable"));
        calendar.DaysToResults(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyDictionary<Guid, ResultsAnswer>>(_ => throw new InvalidOperationException("away"));
        (await handler.HandleAsync(new GetEventFilterQuery(), Ct)).NotActingBecause.Select(r => r.Code).ShouldBe(["switch_off", "calendar_unreadable"]);
        decisions.Unreadable = true;
        (await handler.HandleAsync(new GetEventFilterQuery(), Ct)).NotActingBecause.Select(r => r.Code).ShouldContain("history_unreadable");
    }

    // ---- the auto option

    private static (ApplyEventFilterAutoHandler Handler, FakeDecisions Decisions, Events Events) Auto(EventFilterVerdict? verdict, bool throws = false)
    {
        var decisions = new FakeDecisions();
        var events = new Events();
        return (new ApplyEventFilterAutoHandler(decisions, new Verdict(verdict, throws), events, new Unit(), TimeProvider.System, NullLogger<ApplyEventFilterAutoHandler>.Instance), decisions, events);
    }

    private static EventFilterRow OwnerRow(long id, bool enabled, string approval, int days = 2) =>
        new(id, enabled, days, approval, "owner", Owner, "r", "{}", null, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task A_keep_verdict_with_approval_auto_turns_the_filter_on_as_an_auto_row_with_the_snapshot_and_the_verdict_reference_never_as_the_owner()
    {
        var (handler, decisions, events) = Auto(new EventFilterVerdict("KEEP", "evf-1", "pf-1"));
        decisions.Rows.Add(OwnerRow(1, false, "auto", 4));

        var result = await handler.HandleAsync(new ApplyEventFilterAutoCommand(), Ct);

        result.Value.ShouldBe(new EventFilterAutoResult("turn_on", "keep"));
        var row = decisions.Rows.Last();
        (row.Enabled, row.ResultsWithinDays, row.Approval, row.DecidedBy, row.Actor, row.VerdictRef).ShouldBe((true, 4, "auto", "auto", "auto:event-filter", "pf-1"));
        row.SettingSnapshot.ShouldContain("\"verdict.state\":\"KEEP\"");
        row.SettingSnapshot.ShouldContain("\"verdict.rule_version\":\"evf-1\"");
        events.Recorded.ShouldHaveSingleItem().Actor.ShouldBe("auto:event-filter");
        events.Recorded[0].Detail.ShouldEndWith("decided_by=auto");
    }

    [Theory]
    [InlineData("DROP")]
    [InlineData("INCONCLUSIVE")]
    [InlineData("NOT_EVALUABLE")]
    public async Task Any_verdict_but_keep_changes_nothing_while_the_filter_is_off(string state)
    {
        var (handler, decisions, events) = Auto(new EventFilterVerdict(state, "evf-1", "pf-1"));
        decisions.Rows.Add(OwnerRow(1, false, "auto"));

        (await handler.HandleAsync(new ApplyEventFilterAutoCommand(), Ct)).Value.Action.ShouldBe("none");

        decisions.Rows.Count.ShouldBe(1);
        events.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_approval_manual_even_a_keep_verdict_changes_nothing()
    {
        var (handler, decisions, _) = Auto(new EventFilterVerdict("KEEP", "evf-1", "pf-1"));
        decisions.Rows.Add(OwnerRow(1, false, "manual"));

        (await handler.HandleAsync(new ApplyEventFilterAutoCommand(), Ct)).Value.ShouldBe(new EventFilterAutoResult("none", "approval_manual"));
        decisions.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Until_a_verdict_exists_the_default_port_answers_unavailable_and_the_auto_path_cannot_fire_even_with_approval_auto()
    {
        var decisions = new FakeDecisions();
        decisions.Rows.Add(OwnerRow(1, false, "auto"));
        var handler = new ApplyEventFilterAutoHandler(decisions, new UnavailableEventFilterVerdict(), new Events(), new Unit(), TimeProvider.System, NullLogger<ApplyEventFilterAutoHandler>.Instance);

        (await new UnavailableEventFilterVerdict().NewestAsync("evf-1", Ct)).ShouldBeNull();
        (await handler.HandleAsync(new ApplyEventFilterAutoCommand(), Ct)).Value.ShouldBe(new EventFilterAutoResult("none", "verdict_unavailable"));
        decisions.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_verdict_port_that_throws_is_unavailable_and_changes_nothing()
    {
        var (handler, decisions, _) = Auto(null, throws: true);
        decisions.Rows.Add(OwnerRow(1, false, "auto"));

        (await handler.HandleAsync(new ApplyEventFilterAutoCommand(), Ct)).Value.Action.ShouldBe("none");
        decisions.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_auto_enabled_filter_turns_itself_off_when_the_verdict_stops_being_keep_and_an_owner_enabled_one_does_not()
    {
        var (handler, decisions, _) = Auto(new EventFilterVerdict("DROP", "evf-1", "pf-2"));
        decisions.Rows.Add(new EventFilterRow(1, true, 2, "auto", "auto", "auto:event-filter", "r", "{}", "pf-1", DateTimeOffset.UnixEpoch));
        var (ownerHandler, ownerDecisions, _) = Auto(new EventFilterVerdict("DROP", "evf-1", "pf-2"));
        ownerDecisions.Rows.Add(OwnerRow(1, true, "auto"));

        (await handler.HandleAsync(new ApplyEventFilterAutoCommand(), Ct)).Value.ShouldBe(new EventFilterAutoResult("turn_off", "not_keep"));
        (await ownerHandler.HandleAsync(new ApplyEventFilterAutoCommand(), Ct)).Value.ShouldBe(new EventFilterAutoResult("none", "owner_enabled"));

        decisions.Rows.Last().ShouldSatisfyAllConditions(r => r.Enabled.ShouldBeFalse(), r => r.DecidedBy.ShouldBe("auto"), r => r.VerdictRef.ShouldBe("pf-2"));
        ownerDecisions.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_database_that_refuses_the_auto_row_changes_nothing_and_is_counted_not_applied()
    {
        var (handler, decisions, events) = Auto(new EventFilterVerdict("KEEP", "evf-1", "pf-1"));
        decisions.Rows.Add(OwnerRow(1, false, "auto"));
        decisions.Refuse = "the filter is already on";

        (await handler.HandleAsync(new ApplyEventFilterAutoCommand(), Ct)).Value.ShouldBe(new EventFilterAutoResult("none", "database_refused"));
        decisions.Rows.Count.ShouldBe(1);
        events.Recorded.ShouldBeEmpty();
    }
}
