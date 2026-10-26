using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Depth;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Depth;

/// <summary>The owner's switch (P7-16 criterion 6): owner-only, a reason, an insert-only row and an account event; and the read with the reasons the recorder is not running.</summary>
public sealed class DepthRecorderSwitchTests
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

    private static (ChangeDepthRecorderHandler Handler, FakeDepthSwitchStore Store, Events Events) Change()
    {
        var store = new FakeDepthSwitchStore();
        var events = new Events();
        return (new ChangeDepthRecorderHandler(store, events, new Unit(), TimeProvider.System, NullLogger<ChangeDepthRecorderHandler>.Instance), store, events);
    }

    [Fact]
    public async Task The_owner_switches_it_on_with_a_reason_a_row_is_appended_and_one_account_event_is_recorded_without_the_reason()
    {
        var (handler, store, events) = Change();

        var result = await handler.HandleAsync(new ChangeDepthRecorderCommand(Owner, true, "  record six months for P13  "), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Enabled.ShouldBeTrue();
        var row = store.Rows.ShouldHaveSingleItem();
        row.Actor.ShouldBe(Owner);
        row.Reason.ShouldBe("record six months for P13");
        var recorded = events.Recorded.ShouldHaveSingleItem();
        recorded.Event.ShouldBe(AccountEventType.DepthRecorderSwitched);
        recorded.Actor.ShouldBe(Owner);
        recorded.Detail.ShouldNotBeNull().ShouldNotContain("P13");
        recorded.Detail.ShouldContain("on");
    }

    [Fact]
    public async Task Switching_it_off_is_a_row_too_and_rows_are_never_edited()
    {
        var (handler, store, _) = Change();
        await handler.HandleAsync(new ChangeDepthRecorderCommand(Owner, true, "on"), Ct);

        await handler.HandleAsync(new ChangeDepthRecorderCommand(Owner, false, "off"), Ct);

        store.Rows.Select(r => r.Enabled).ShouldBe([true, false]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("service:feed")]
    [InlineData("owner:")]
    [InlineData("OWNER:x@example.invalid")]
    public async Task A_actor_that_is_not_the_signed_in_owner_is_refused_and_nothing_is_written(string actor)
    {
        var (handler, store, events) = Change();

        var result = await handler.HandleAsync(new ChangeDepthRecorderCommand(actor, true, "reason"), Ct);

        result.Error.Code.ShouldBe("depth.owner_only");
        store.Rows.ShouldBeEmpty();
        events.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_reason_is_refused_and_nothing_is_written(string reason)
    {
        var (handler, store, _) = Change();

        var result = await handler.HandleAsync(new ChangeDepthRecorderCommand(Owner, true, reason), Ct);

        result.Error.Code.ShouldBe("depth.reason_invalid");
        store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_refused()
    {
        var (handler, _, _) = Change();

        var result = await handler.HandleAsync(new ChangeDepthRecorderCommand(Owner, true, new string('x', 501)), Ct);

        result.Error.Code.ShouldBe("depth.reason_invalid");
    }

    [Fact]
    public async Task A_failing_audit_write_does_not_undo_the_row()
    {
        var store = new FakeDepthSwitchStore();
        var events = new Events();
        var handler = new ChangeDepthRecorderHandler(store, events, new Unit(fail: true), TimeProvider.System, NullLogger<ChangeDepthRecorderHandler>.Instance);

        var result = await handler.HandleAsync(new ChangeDepthRecorderCommand(Owner, true, "on"), Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Rows.Count.ShouldBe(1);
    }

    // ---- the read ----

    private static (GetDepthRecorderHandler Handler, FakeDepthSwitchStore Store, FakeDepthHeartbeats Beats, InMemoryServiceData Data) Read()
    {
        var store = new FakeDepthSwitchStore();
        var beats = new FakeDepthHeartbeats();
        var data = new InMemoryServiceData();
        var reader = new ServiceSettingsReader(new ServiceRegistry([]), data);
        return (new GetDepthRecorderHandler(store, beats, reader), store, beats, data);
    }

    private static DepthStatus Status(string state, bool deployment = true, bool alive = true) =>
        new(state, DateTimeOffset.UnixEpoch, alive, new DepthHeartbeat(deployment, 0, 0, 0, 0, 0, 0, true));

    [Fact]
    public async Task With_nothing_stored_the_switch_is_off_and_the_reasons_name_each_closed_gate()
    {
        var (handler, _, beats, _) = Read();
        beats.Status = Status(DepthStates.Disabled, deployment: false);

        var view = await handler.HandleAsync(new GetDepthRecorderQuery(), Ct);

        view.Enabled.ShouldBeFalse();
        view.SwitchId.ShouldBeNull();
        view.State.ShouldBe(DepthStates.Disabled);
        view.NotRunningBecause.Count.ShouldBe(3);
        view.NotRunningBecause[0].ShouldContain("Depth__Enabled");
        view.NotRunningBecause[1].ShouldContain("OFF");
        view.NotRunningBecause[2].ShouldContain("paused");
        view.Notice.ShouldContain("nothing reads it");
    }

    [Fact]
    public async Task With_all_three_gates_open_the_reasons_are_empty_and_the_history_is_newest_first()
    {
        var (handler, store, beats, data) = Read();
        store.Set(false);
        store.Set(true);
        data.Store(DepthRecorderService.Id, DepthRecorderService.Definition.Defaults with { Enabled = true });
        beats.Status = Status(DepthStates.Recording);

        var view = await handler.HandleAsync(new GetDepthRecorderQuery(), Ct);

        view.Enabled.ShouldBeTrue();
        view.SwitchId.ShouldBe(2);
        view.NotRunningBecause.ShouldBeEmpty();
        view.History.Select(h => h.Id).ShouldBe([2, 1]);
    }

    [Theory]
    [InlineData(DepthStates.NotSubscribed, "806")]
    [InlineData(DepthStates.DiskBudget, "cap")]
    [InlineData(DepthStates.DiskFree, "floor")]
    [InlineData(DepthStates.TokenRefused, "token")]
    public async Task A_stuck_recorder_says_why_from_its_state_word(string state, string fragment)
    {
        var (handler, store, beats, data) = Read();
        store.Set(true);
        data.Store(DepthRecorderService.Id, DepthRecorderService.Definition.Defaults with { Enabled = true });
        beats.Status = Status(state);

        var view = await handler.HandleAsync(new GetDepthRecorderQuery(), Ct);

        view.NotRunningBecause.ShouldHaveSingleItem().ShouldContain(fragment);
    }

    [Fact]
    public async Task A_feed_host_that_never_reported_is_said_so()
    {
        var (handler, store, _, data) = Read();
        store.Set(true);
        data.Store(DepthRecorderService.Id, DepthRecorderService.Definition.Defaults with { Enabled = true });

        var view = await handler.HandleAsync(new GetDepthRecorderQuery(), Ct);

        view.State.ShouldBeNull();
        view.NotRunningBecause.ShouldHaveSingleItem().ShouldContain("not reported");
    }
}
