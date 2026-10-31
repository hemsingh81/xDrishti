using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Ticks;
using XDrishti.Application.Depth;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Application.Ticks;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Audit;

namespace XDrishti.Application.Tests.Ticks;

/// <summary>The owner's tick switch (P7-16b criterion 6, as P7-16's): owner-only, a reason, an insert-only row and an account event; and the read with the reasons the recorder is not running.</summary>
public sealed class TickRecorderSwitchTests
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

    private static (ChangeTickRecorderHandler Handler, FakeTickSwitchStore Store, Events Events) Change(bool auditFails = false)
    {
        var store = new FakeTickSwitchStore();
        var events = new Events();
        return (new ChangeTickRecorderHandler(store, events, new Unit(auditFails), TimeProvider.System, NullLogger<ChangeTickRecorderHandler>.Instance), store, events);
    }

    [Fact]
    public async Task The_owner_switches_it_on_with_a_reason_one_row_and_one_service_event_without_the_reason()
    {
        var (handler, store, events) = Change();

        var result = await handler.HandleAsync(new ChangeTickRecorderCommand(Owner, true, "  record ticks for P13  "), Ct);

        result.Value.Enabled.ShouldBeTrue();
        store.Rows.ShouldHaveSingleItem().Reason.ShouldBe("record ticks for P13");
        var recorded = events.Recorded.ShouldHaveSingleItem();
        recorded.Event.ShouldBe(AccountEventType.TickRecorderSwitched);
        recorded.Detail.ShouldBe($"switch={result.Value.Id} tick_recorder=on");
        AuditKinds.CategoryOf(AccountEventType.TickRecorderSwitched).ShouldBe(AuditKinds.Service);
    }

    [Theory]
    [InlineData("service:feed")]
    [InlineData("owner:")]
    [InlineData("")]
    public async Task A_non_owner_actor_is_refused_and_nothing_is_written(string actor)
    {
        var (handler, store, events) = Change();

        var result = await handler.HandleAsync(new ChangeTickRecorderCommand(actor, true, "x"), Ct);

        result.Error.Code.ShouldBe("ticks.owner_only");
        store.Rows.ShouldBeEmpty();
        events.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_reason_is_refused(string reason)
    {
        var (handler, store, _) = Change();

        (await handler.HandleAsync(new ChangeTickRecorderCommand(Owner, true, reason), Ct)).Error.Code.ShouldBe("ticks.reason_invalid");
        (await handler.HandleAsync(new ChangeTickRecorderCommand(Owner, true, new string('r', 501)), Ct)).Error.Code.ShouldBe("ticks.reason_invalid");
        store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_audit_write_leaves_the_row_standing()
    {
        var (handler, store, _) = Change(auditFails: true);

        var result = await handler.HandleAsync(new ChangeTickRecorderCommand(Owner, false, "stop"), Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Rows.ShouldHaveSingleItem().Enabled.ShouldBeFalse();
    }

    private static (GetTickRecorderHandler Handler, FakeTickSwitchStore Store, FakeTickHeartbeats Beats, InMemoryServiceData Data) Read()
    {
        var store = new FakeTickSwitchStore();
        var beats = new FakeTickHeartbeats();
        var data = new InMemoryServiceData();
        return (new GetTickRecorderHandler(store, beats, new ServiceSettingsReader(new ServiceRegistry([]), data)), store, beats, data);
    }

    private static TickStatus Status(string state, bool deployment = true) => new(state, DateTimeOffset.UnixEpoch, true, new TickHeartbeat(deployment, 0, 0, 0, 0, 0, 0, 0, true));

    [Fact]
    public async Task With_nothing_stored_the_reasons_name_each_closed_gate()
    {
        var (handler, _, beats, _) = Read();
        beats.Status = Status(DepthStates.Disabled, deployment: false);

        var view = await handler.HandleAsync(new GetTickRecorderQuery(), Ct);

        view.Enabled.ShouldBeFalse();
        view.SwitchId.ShouldBeNull();
        view.NotRunningBecause.Count.ShouldBe(3);
        view.NotRunningBecause[0].ShouldContain("Ticks__Enabled");
        view.NotRunningBecause[1].ShouldContain("OFF");
        view.NotRunningBecause[2].ShouldContain("tick-recorder");
        view.Notice.ShouldContain("nothing reads them");
    }

    [Fact]
    public async Task With_all_three_gates_open_the_reasons_are_empty_and_the_history_is_newest_first()
    {
        var (handler, store, beats, data) = Read();
        store.Set(false);
        store.Set(true);
        data.Store(TickRecorderService.Id, TickRecorderService.Definition.Defaults with { Enabled = true });
        beats.Status = Status(DepthStates.Recording);

        var view = await handler.HandleAsync(new GetTickRecorderQuery(), Ct);

        view.Enabled.ShouldBeTrue();
        view.NotRunningBecause.ShouldBeEmpty();
        view.History.Select(h => h.Id).ShouldBe([2L, 1L]);
    }

    [Fact]
    public async Task A_waiting_recorder_says_it_waits_for_the_depth_set()
    {
        var (handler, store, beats, data) = Read();
        store.Set(true);
        data.Store(TickRecorderService.Id, TickRecorderService.Definition.Defaults with { Enabled = true });
        beats.Status = Status(TickSetChoice.WaitingForSet);

        (await handler.HandleAsync(new GetTickRecorderQuery(), Ct)).NotRunningBecause.ShouldHaveSingleItem().ShouldContain("09:14:30");
    }
}
