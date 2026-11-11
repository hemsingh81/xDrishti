using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.OpeningBars;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Tests.Platform.Services;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Audit;

namespace XDrishti.Application.Tests.OpeningBars;

/// <summary>The owner's opening-bars switch (BK-03b): owner-only, a reason, an insert-only row and an account event; the read with the reasons the recorder is not running; the daily retention call.</summary>
public sealed class OpeningBarsSwitchTests
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

    private static (ChangeOpeningBarsHandler Handler, FakeOpeningSwitch Store, Events Events) Change(bool auditFails = false)
    {
        var store = new FakeOpeningSwitch();
        var events = new Events();
        return (new ChangeOpeningBarsHandler(store, events, new Unit(auditFails), TimeProvider.System, NullLogger<ChangeOpeningBarsHandler>.Instance), store, events);
    }

    [Fact]
    public async Task The_owner_switches_it_on_with_a_reason_one_row_and_one_service_event_without_the_reason()
    {
        var (handler, store, events) = Change();

        var result = await handler.HandleAsync(new ChangeOpeningBarsCommand(Owner, true, "  form the opening baskets  "), Ct);

        result.Value.Enabled.ShouldBeTrue();
        store.Rows.ShouldHaveSingleItem().Reason.ShouldBe("form the opening baskets");
        var recorded = events.Recorded.ShouldHaveSingleItem();
        recorded.Event.ShouldBe(AccountEventType.OpeningBarsSwitched);
        recorded.Detail.ShouldBe($"switch={result.Value.Id} opening_bars=on");
        AuditKinds.CategoryOf(AccountEventType.OpeningBarsSwitched).ShouldBe(AuditKinds.Service);
    }

    [Theory]
    [InlineData("service:feed")]
    [InlineData("owner:")]
    [InlineData("")]
    public async Task A_non_owner_actor_is_refused_and_nothing_is_written(string actor)
    {
        var (handler, store, events) = Change();

        var result = await handler.HandleAsync(new ChangeOpeningBarsCommand(actor, true, "x"), Ct);

        result.Error.Code.ShouldBe("opening_bars.owner_only");
        store.Rows.ShouldBeEmpty();
        events.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_or_over_long_reason_is_refused(string reason)
    {
        var (handler, store, _) = Change();

        var missing = await handler.HandleAsync(new ChangeOpeningBarsCommand(Owner, true, reason), Ct);
        var tooLong = await handler.HandleAsync(new ChangeOpeningBarsCommand(Owner, true, new string('x', 501)), Ct);

        missing.Error.Code.ShouldBe("opening_bars.reason_invalid");
        tooLong.Error.Code.ShouldBe("opening_bars.reason_invalid");
        store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failing_audit_write_does_not_undo_the_row()
    {
        var (handler, store, _) = Change(auditFails: true);

        var result = await handler.HandleAsync(new ChangeOpeningBarsCommand(Owner, false, "stop"), Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Rows.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_read_says_off_with_the_reasons_on_a_fresh_system_and_on_with_the_history_after_a_switch()
    {
        var store = new FakeOpeningSwitch();
        var data = new InMemoryServiceData();
        var services = new MiniProvider().Singleton(new ServiceRegistry([])).Singleton<IServiceScheduleStore>(data).Singleton<IServiceRunStore>(data).Scoped<ServiceSettingsReader>();
        using var scope = services.CreateScope();
        var handler = new GetOpeningBarsRecorderHandler(store, data, scope.ServiceProvider.GetRequiredService<ServiceSettingsReader>());

        var fresh = await handler.HandleAsync(new GetOpeningBarsRecorderQuery(), Ct);
        await store.AppendAsync(true, Owner, "go", Ct);
        data.Store(OpeningBarsService.Id, OpeningBarsService.Definition.Defaults with { Enabled = true });
        var on = await handler.HandleAsync(new GetOpeningBarsRecorderQuery(), Ct);

        fresh.Enabled.ShouldBeFalse();
        fresh.State.ShouldBeNull();
        fresh.NotRunningBecause.Count.ShouldBe(3);
        on.Enabled.ShouldBeTrue();
        on.SwitchId.ShouldBe(1);
        on.History.Single().Actor.ShouldBe(Owner);
        on.NotRunningBecause.ShouldNotContain("Your switch is OFF.");
    }
}
