#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Selection;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Selection;

/// <summary>BK-08 criteria 10 and 11: every change is the owner's with a fresh password, nothing is stored when it is refused, each key is recorded with its direction, and an AccountEvent is audited without a password.</summary>
public sealed class BasketSettingsHandlersTests
{
    private const string Owner = "owner:hem";

    private static readonly Guid GateBasket = new("00000000-0000-0000-0000-0000000000b1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ICommandHandler<StepUpCommand, StepUpResult> Password(bool ok)
    {
        var password = Substitute.For<ICommandHandler<StepUpCommand, StepUpResult>>();
        password.HandleAsync(Arg.Any<StepUpCommand>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(ok ? Result.Success(new StepUpResult(DateTimeOffset.UnixEpoch)) : Result.Failure<StepUpResult>(AuthErrors.InvalidCredentials)));
        return password;
    }

    private static ChangeBasketSettingsCommand Command(string actor, string? source = null, double? cap = null, bool clear = false, double? rho = null, string? password = "pw") =>
        new(actor, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), source, cap, clear, rho, password);

    private static ChangeBasketSettingsHandler Handler(FakeBasketSettingsStore store, bool passwordOk = true, bool basketExists = true, IAccountEventLog? events = null, ICommandHandler<StepUpCommand, StepUpResult>? password = null)
    {
        var baskets = Substitute.For<IBasketStore>();
        baskets.FindAsync(GateBasket, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(basketExists ? (Basket?)Basket.CreateSystem("Nifty 50", BasketKinds.Index, DateTimeOffset.UnixEpoch) : null));
        return new ChangeBasketSettingsHandler(store, baskets, password ?? Password(passwordOk), events ?? Substitute.For<IAccountEventLog>(), Substitute.For<IAccountUnitOfWork>(), TimeProvider.System, NullLogger<ChangeBasketSettingsHandler>.Instance);
    }

    [Fact]
    public async Task With_nothing_stored_everything_is_off()
    {
        var view = await new GetBasketSettingsHandler(new FakeBasketSettingsStore()).HandleAsync(new GetBasketSettingsQuery(), Ct);

        (view.BreadthSource, view.MaxClusterRiskPct, view.ClusterRho, view.Version).ShouldBe(("tracked", null, 0.70, 0));
        view.History.ShouldBeEmpty();
        view.LatestLoosening.ShouldBeNull();
    }

    [Theory]
    [InlineData("system:nightly")]
    [InlineData("rule:cluster")]
    [InlineData("cli")]
    [InlineData("")]
    public async Task A_non_owner_actor_is_refused_before_the_password_and_the_store(string actor)
    {
        var store = new FakeBasketSettingsStore();
        var password = Password(true);

        var result = await Handler(store, password: password).HandleAsync(Command(actor, cap: 1.0), Ct);

        result.Error.Code.ShouldBe("selection.owner_only");
        store.Appends.ShouldBe(0);
        await password.DidNotReceive().HandleAsync(Arg.Any<StepUpCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_wrong_or_missing_password_stores_nothing()
    {
        var store = new FakeBasketSettingsStore();

        var result = await Handler(store, passwordOk: false).HandleAsync(Command(Owner, cap: 1.0, password: null), Ct);

        result.Error.Code.ShouldBe(AuthErrors.InvalidCredentials.Code);
        store.Appends.ShouldBe(0);
    }

    [Fact]
    public async Task The_password_is_checked_for_this_change_without_opening_the_elevation_window()
    {
        var password = Password(true);

        await Handler(new FakeBasketSettingsStore(), password: password).HandleAsync(Command(Owner, cap: 1.0), Ct);

        await password.Received(1).HandleAsync(Arg.Is<StepUpCommand>(c => !c.Elevate && c.Password == "pw"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_tightening_and_a_loosening_both_need_the_password_and_are_recorded_with_their_direction()
    {
        var store = new FakeBasketSettingsStore();
        var password = Password(true);
        var handler = Handler(store, password: password);

        var set = await handler.HandleAsync(Command(Owner, cap: 1.0), Ct);
        var raised = await handler.HandleAsync(Command(Owner, cap: 1.5), Ct);
        var cleared = await handler.HandleAsync(Command(Owner, clear: true), Ct);

        set.Value.History[0].Direction.ShouldBe("tighten");
        raised.Value.History[0].Direction.ShouldBe("loosen");
        (cleared.Value.History[0].Direction, cleared.Value.MaxClusterRiskPct).ShouldBe(("loosen", null));
        cleared.Value.History.ShouldAllBe(h => h.Actor == Owner);
        cleared.Value.LatestLoosening!.Key.ShouldBe("selection.max_cluster_risk_pct");
        await password.Received(3).HandleAsync(Arg.Any<StepUpCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Choosing_a_basket_as_the_breadth_source_is_a_loosening_and_a_missing_basket_is_refused()
    {
        var store = new FakeBasketSettingsStore();

        var ok = await Handler(store).HandleAsync(Command(Owner, source: BasketSettings.SourceOf(GateBasket)), Ct);
        var missing = await Handler(new FakeBasketSettingsStore(), basketExists: false).HandleAsync(Command(Owner, source: BasketSettings.SourceOf(GateBasket)), Ct);

        (ok.Value.BreadthSource, ok.Value.History[0].Direction).ShouldBe((BasketSettings.SourceOf(GateBasket), "loosen"));
        missing.Error.Code.ShouldBe("selection.basket_not_found");
    }

    [Fact]
    public async Task A_value_out_of_range_or_a_change_of_nothing_stores_nothing()
    {
        var store = new FakeBasketSettingsStore();
        var handler = Handler(store);

        (await handler.HandleAsync(Command(Owner, cap: 0), Ct)).Error.Code.ShouldBe("selection.invalid");
        (await handler.HandleAsync(Command(Owner, source: "basket:x"), Ct)).Error.Code.ShouldBe("selection.invalid");
        (await handler.HandleAsync(Command(Owner, source: "tracked"), Ct)).Error.Code.ShouldBe("selection.invalid");

        store.Appends.ShouldBe(0);
    }

    [Fact]
    public async Task A_settings_row_written_meanwhile_makes_the_change_a_conflict()
    {
        var store = new FakeBasketSettingsStore();
        var racing = new RacingStore(store);
        var baskets = Substitute.For<IBasketStore>();

        var result = await new ChangeBasketSettingsHandler(racing, baskets, Password(true), Substitute.For<IAccountEventLog>(), Substitute.For<IAccountUnitOfWork>(), TimeProvider.System, NullLogger<ChangeBasketSettingsHandler>.Instance).HandleAsync(Command(Owner, cap: 1.0), Ct);

        result.Error.Code.ShouldBe("selection.stale");
    }

    [Fact]
    public async Task Each_accepted_key_writes_one_audit_event_that_never_holds_the_password()
    {
        var events = Substitute.For<IAccountEventLog>();
        var recorded = new List<AccountEvent>();
        events.When(e => e.Record(Arg.Any<AccountEvent>())).Do(c => recorded.Add(c.Arg<AccountEvent>()));

        await Handler(new FakeBasketSettingsStore(), events: events).HandleAsync(Command(Owner, cap: 1.0, rho: 0.8, password: "s3cret-pw"), Ct);

        recorded.Count.ShouldBe(2);
        recorded.ShouldAllBe(e => e.Event == AccountEventType.BasketSettingsChanged && e.Actor == Owner);
        recorded.ShouldAllBe(e => !e.Detail!.Contains("s3cret-pw", StringComparison.Ordinal));
        recorded[0].Detail!.ShouldContain("direction=tighten");
    }

    private sealed class RacingStore(FakeBasketSettingsStore inner) : XDrishti.Application.Abstractions.Selection.IBasketSettingsStore
    {
        public ValueTask<XDrishti.Application.Abstractions.Selection.BasketSettingsState> CurrentAsync(CancellationToken cancellationToken) => inner.CurrentAsync(cancellationToken);

        public ValueTask<IReadOnlyList<XDrishti.Application.Abstractions.Selection.BasketSettingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken) => inner.HistoryAsync(limit, cancellationToken);

        public ValueTask<bool> AppendAsync(IReadOnlyList<BasketSettingChange> rows, long expectedVersion, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
