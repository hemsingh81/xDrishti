using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.OptionBackfill;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Recording;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Audit;

namespace XDrishti.Application.Tests.OptionBackfill;

/// <summary>The owner's backfill switch and probe acceptance (P14-03 criteria 6 and 9, as P14-02's): owner-only, a reason, an insert-only row and an account event; and the read with the reasons the backfill is not running and the progress.</summary>
public sealed class OptionBackfillSwitchTests
{
    private const string Owner = "owner:hem@example.invalid";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

        public ValueTask<XDrishti.Domain.Common.Result> RunSerializedAsync(Func<CancellationToken, ValueTask<XDrishti.Domain.Common.Result>> work, CancellationToken cancellationToken) => work(cancellationToken);
    }

    private static (ChangeOptionBackfillHandler Handler, FakeBackfillSwitchStore Store, Events Events) Change(bool auditFails = false)
    {
        var store = new FakeBackfillSwitchStore();
        var events = new Events();
        return (new ChangeOptionBackfillHandler(store, events, new Unit(auditFails), TimeProvider.System, NullLogger<ChangeOptionBackfillHandler>.Instance), store, events);
    }

    [Fact]
    public async Task The_owner_switches_it_on_with_a_reason_one_row_and_one_service_event_without_the_reason()
    {
        var (handler, store, events) = Change();

        var result = await handler.HandleAsync(new ChangeOptionBackfillCommand(Owner, true, "  backfill five years for research  "), Ct);

        result.Value.Enabled.ShouldBeTrue();
        store.Rows.ShouldHaveSingleItem().Reason.ShouldBe("backfill five years for research");
        var recorded = events.Recorded.ShouldHaveSingleItem();
        recorded.Event.ShouldBe(AccountEventType.OptionBackfillSwitched);
        recorded.Detail.ShouldBe($"switch={result.Value.Id} option_backfill=on");
        AuditKinds.CategoryOf(AccountEventType.OptionBackfillSwitched).ShouldBe(AuditKinds.Service);
        AuditKinds.CategoryOf(AccountEventType.OptionBackfillProbeAccepted).ShouldBe(AuditKinds.Service);
    }

    [Theory]
    [InlineData("service:feed")]
    [InlineData("owner:")]
    [InlineData("")]
    public async Task A_non_owner_actor_is_refused_and_nothing_is_written(string actor)
    {
        var (handler, store, events) = Change();

        (await handler.HandleAsync(new ChangeOptionBackfillCommand(actor, true, "x"), Ct)).Error.Code.ShouldBe("options.owner_only");

        store.Rows.ShouldBeEmpty();
        events.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_or_over_long_reason_is_refused(string reason)
    {
        var (handler, store, _) = Change();

        (await handler.HandleAsync(new ChangeOptionBackfillCommand(Owner, true, reason), Ct)).Error.Code.ShouldBe("options.reason_invalid");
        (await handler.HandleAsync(new ChangeOptionBackfillCommand(Owner, true, new string('r', 501)), Ct)).Error.Code.ShouldBe("options.reason_invalid");

        store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_audit_write_leaves_the_row_standing()
    {
        var (handler, store, _) = Change(auditFails: true);

        (await handler.HandleAsync(new ChangeOptionBackfillCommand(Owner, false, "stop"), Ct)).IsSuccess.ShouldBeTrue();

        store.Rows.ShouldHaveSingleItem().Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task The_owner_accepts_the_probe_result_with_a_note_one_row_and_one_event()
    {
        var store = new FakeProbeAckStore();
        var events = new Events();
        var handler = new AcceptBackfillProbeHandler(store, events, new Unit(), TimeProvider.System, NullLogger<AcceptBackfillProbeHandler>.Instance);

        var result = await handler.HandleAsync(new AcceptBackfillProbeCommand(Owner, "table of ADR 0177 filled"), Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Rows.ShouldHaveSingleItem().Note.ShouldBe("table of ADR 0177 filled");
        events.Recorded.ShouldHaveSingleItem().Event.ShouldBe(AccountEventType.OptionBackfillProbeAccepted);
        (await handler.HandleAsync(new AcceptBackfillProbeCommand("service:feed", "x"), Ct)).Error.Code.ShouldBe("options.owner_only");
        (await handler.HandleAsync(new AcceptBackfillProbeCommand(Owner, " "), Ct)).Error.Code.ShouldBe("options.reason_invalid");
        store.Rows.Count.ShouldBe(1);
    }

    private static (GetOptionBackfillHandler Handler, FakeBackfillSwitchStore Store, FakeProbeAckStore Ack, FakeBackfillHeartbeats Beats, InMemoryServiceData Data) Read()
    {
        var store = new FakeBackfillSwitchStore();
        var ack = new FakeProbeAckStore();
        var beats = new FakeBackfillHeartbeats();
        var data = new InMemoryServiceData();
        return (new GetOptionBackfillHandler(store, ack, beats, new ServiceSettingsReader(new ServiceRegistry([]), data)), store, ack, beats, data);
    }

    private static BackfillStatus Status(string state, bool deployment = true, BackfillProgress? progress = null) =>
        new(state, DateTimeOffset.UnixEpoch, true, new BackfillHeartbeat(deployment, 1, 0, 0, 0, 0, true, progress));

    [Fact]
    public async Task With_nothing_stored_the_reasons_name_each_closed_gate()
    {
        var (handler, _, _, beats, _) = Read();
        beats.Status = Status(RecorderStates.Disabled, deployment: false);

        var view = await handler.HandleAsync(new GetOptionBackfillQuery(), Ct);

        view.Enabled.ShouldBeFalse();
        view.ProbeAccepted.ShouldBeNull();
        view.NotRunningBecause.Count.ShouldBe(4);
        view.NotRunningBecause[0].ShouldContain("Options__Backfill__Enabled");
        view.NotRunningBecause[1].ShouldContain("OFF");
        view.NotRunningBecause[2].ShouldContain("probe result");
        view.NotRunningBecause[3].ShouldContain("options-backfill");
        view.Notice.ShouldContain("nothing reads them");
    }

    [Fact]
    public async Task With_all_gates_open_the_reasons_are_empty_the_history_is_newest_first_and_the_progress_has_an_estimate()
    {
        var (handler, store, ack, beats, data) = Read();
        store.Set(false);
        store.Set(true);
        ack.Accept();
        data.Store(OptionBackfillService.Id, OptionBackfillService.Definition.Defaults with { Enabled = true });
        beats.Status = Status(BackfillStates.Backfilling, progress: new BackfillProgress(32_000, 10_000, 100, 50, 3, 21_997));

        var view = await handler.HandleAsync(new GetOptionBackfillQuery(), Ct);

        view.Enabled.ShouldBeTrue();
        view.NotRunningBecause.ShouldBeEmpty();
        view.History.Select(h => h.Id).ShouldBe([2L, 1L]);
        view.ProbeAccepted.ShouldNotBeNull();
        view.Progress.ShouldNotBeNull().ShouldSatisfyAllConditions(p => p.Left.ShouldBe(21_997), p => p.GivenUp.ShouldBe(3), p => p.EstimatedHours.ShouldBe(6.1));
    }

    [Fact]
    public async Task A_disk_stop_says_nothing_was_deleted_and_the_daily_cap_says_it_goes_on_tomorrow()
    {
        var (handler, store, ack, beats, data) = Read();
        store.Set(true);
        ack.Accept();
        data.Store(OptionBackfillService.Id, OptionBackfillService.Definition.Defaults with { Enabled = true });

        beats.Status = Status(RecorderStates.DiskBudget);
        (await handler.HandleAsync(new GetOptionBackfillQuery(), Ct)).NotRunningBecause.ShouldHaveSingleItem().ShouldContain("nothing was deleted");
        beats.Status = Status(BackfillStates.DailyCap);
        (await handler.HandleAsync(new GetOptionBackfillQuery(), Ct)).NotRunningBecause.ShouldHaveSingleItem().ShouldContain("next India day");
    }
}
