#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Sizing;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Sizing;

/// <summary>BK-10 criteria 9 and 10: every change is the owner's with a fresh password, nothing is stored when it is refused, each key is recorded with its direction, and a stale form is a conflict.</summary>
public sealed class SizingHandlersTests
{
    private const string Owner = "owner:hem";

    private static readonly Guid BasketId = new("00000000-0000-0000-0000-0000000000c1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ICommandHandler<StepUpCommand, StepUpResult> Password(bool ok)
    {
        var password = Substitute.For<ICommandHandler<StepUpCommand, StepUpResult>>();
        password.HandleAsync(Arg.Any<StepUpCommand>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(ok ? Result.Success(new StepUpResult(DateTimeOffset.UnixEpoch)) : Result.Failure<StepUpResult>(AuthErrors.InvalidCredentials)));
        return password;
    }

    private static ChangeSizingSettingsCommand Settings(string actor, bool? vol = null, double? floor = null, bool? cap = null, double? pct = null, string? password = "pw") =>
        new(actor, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), vol, floor, cap, pct, password);

    private static ChangeBasketRiskBudgetCommand Budget(string actor, double? pct, string? password = "pw") =>
        new(actor, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), BasketId, pct, password);

    private static ChangeSizingSettingsHandler SettingsHandler(FakeSizingStore store, bool passwordOk = true) =>
        new(store, Password(passwordOk), Substitute.For<IAccountEventLog>(), Substitute.For<IAccountUnitOfWork>(), TimeProvider.System, new CostStressOptions(), NullLogger<ChangeSizingSettingsHandler>.Instance);

    private static ChangeBasketRiskBudgetHandler BudgetHandler(FakeSizingStore store, bool passwordOk = true, bool basketExists = true)
    {
        var baskets = Substitute.For<IBasketStore>();
        baskets.FindAsync(BasketId, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(basketExists ? (Basket?)Basket.CreateSystem("Nifty", BasketKinds.Index, DateTimeOffset.UnixEpoch) : null));
        return new ChangeBasketRiskBudgetHandler(store, baskets, Password(passwordOk), Substitute.For<IAccountEventLog>(), Substitute.For<IAccountUnitOfWork>(), TimeProvider.System, NullLogger<ChangeBasketRiskBudgetHandler>.Instance);
    }

    [Fact]
    public async Task With_nothing_stored_the_settings_are_all_off_and_neutral()
    {
        var view = await new GetSizingSettingsHandler(new FakeSizingStore()).HandleAsync(new GetSizingSettingsQuery(), Ct);

        (view.VolTargetEnabled, view.CapacityEnabled, view.Floor, view.CapacityPct, view.MinFit, view.Version).ShouldBe((false, false, 0.5, 0.005, 0.5, 0));
        view.History.ShouldBeEmpty();
        view.LatestLoosening.ShouldBeNull();
    }

    [Theory]
    [InlineData("system:nightly")]
    [InlineData("auto:event-filter")]
    [InlineData("")]
    public async Task A_non_owner_actor_is_refused_before_the_password_and_the_store(string actor)
    {
        var store = new FakeSizingStore();

        var result = await SettingsHandler(store).HandleAsync(Settings(actor, vol: true), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("selection.owner_only");
        store.Appends.ShouldBe(0);
    }

    [Fact]
    public async Task A_wrong_or_missing_password_stores_nothing_and_learns_nothing()
    {
        var store = new FakeSizingStore();

        var result = await SettingsHandler(store, passwordOk: false).HandleAsync(Settings(Owner, vol: true, password: null), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrors.InvalidCredentials.Code);
        store.Appends.ShouldBe(0);
    }

    [Fact]
    public async Task The_password_is_checked_for_this_change_without_opening_the_elevation_window()
    {
        var password = Password(true);
        var handler = new ChangeSizingSettingsHandler(new FakeSizingStore(), password, Substitute.For<IAccountEventLog>(), Substitute.For<IAccountUnitOfWork>(), TimeProvider.System, new CostStressOptions(), NullLogger<ChangeSizingSettingsHandler>.Instance);

        await handler.HandleAsync(Settings(Owner, vol: true), Ct);

        await password.Received(1).HandleAsync(Arg.Is<StepUpCommand>(c => !c.Elevate && c.Password == "pw"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Turning_a_switch_on_appends_a_tighten_row_and_off_again_a_loosen_row_shown_as_the_latest_loosening()
    {
        var store = new FakeSizingStore();
        var handler = SettingsHandler(store);

        var on = await handler.HandleAsync(Settings(Owner, vol: true, cap: true), Ct);
        var off = await handler.HandleAsync(Settings(Owner, vol: false), Ct);

        on.Value.VolTargetEnabled.ShouldBeTrue();
        on.Value.History.Select(h => h.Direction).ShouldBe(["tighten", "tighten"]);
        off.Value.VolTargetEnabled.ShouldBeFalse();
        off.Value.CapacityEnabled.ShouldBeTrue();
        off.Value.History[0].Direction.ShouldBe("loosen");
        off.Value.History[0].Actor.ShouldBe(Owner);
        off.Value.LatestLoosening!.Key.ShouldBe("sizing.vol_target.enabled");
        off.Value.Version.ShouldBe(3);
    }

    [Fact]
    public async Task A_value_out_of_range_or_a_change_of_nothing_stores_nothing()
    {
        var store = new FakeSizingStore();
        var handler = SettingsHandler(store);

        (await handler.HandleAsync(Settings(Owner, floor: 0.1), Ct)).Error.Code.ShouldBe("selection.invalid");
        (await handler.HandleAsync(Settings(Owner, pct: 0.006), Ct)).Error.Code.ShouldBe("selection.invalid");
        (await handler.HandleAsync(Settings(Owner, vol: false), Ct)).Error.Code.ShouldBe("selection.invalid");

        store.Appends.ShouldBe(0);
    }

    [Fact]
    public async Task A_settings_row_written_meanwhile_makes_the_change_a_conflict()
    {
        var store = new FakeSizingStore();
        var stale = new RacingStore(store);

        var result = await new ChangeSizingSettingsHandler(stale, Password(true), Substitute.For<IAccountEventLog>(), Substitute.For<IAccountUnitOfWork>(), TimeProvider.System, new CostStressOptions(), NullLogger<ChangeSizingSettingsHandler>.Instance).HandleAsync(Settings(Owner, vol: true), Ct);

        result.Error.Code.ShouldBe("selection.stale");
    }

    [Fact]
    public async Task Setting_a_budget_records_tighten_raising_it_loosen_and_removing_it_loosen_and_the_budget_is_gone()
    {
        var store = new FakeSizingStore();
        var handler = BudgetHandler(store);

        var set = await handler.HandleAsync(Budget(Owner, 30), Ct);
        var raised = await handler.HandleAsync(Budget(Owner, 40), Ct);
        var removed = await handler.HandleAsync(Budget(Owner, null), Ct);

        set.Value.Budgets.Single().BudgetPct.ShouldBe(30);
        set.Value.History[0].Direction.ShouldBe("tighten");
        raised.Value.History[0].Direction.ShouldBe("loosen");
        removed.Value.History[0].Direction.ShouldBe("loosen");
        removed.Value.Budgets.ShouldBeEmpty();
        removed.Value.History.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_budget_change_by_a_non_owner_or_with_a_wrong_password_or_for_a_missing_basket_stores_nothing()
    {
        var store = new FakeSizingStore();

        (await BudgetHandler(store).HandleAsync(Budget("system:x", 30), Ct)).Error.Code.ShouldBe("selection.owner_only");
        (await BudgetHandler(store, passwordOk: false).HandleAsync(Budget(Owner, 30), Ct)).Error.Code.ShouldBe(AuthErrors.InvalidCredentials.Code);
        (await BudgetHandler(store, basketExists: false).HandleAsync(Budget(Owner, 30), Ct)).Error.Code.ShouldBe("selection.basket_not_found");
        (await BudgetHandler(store).HandleAsync(Budget(Owner, 0.2), Ct)).Error.Code.ShouldBe("selection.invalid");

        store.Appends.ShouldBe(0);
    }

    /// <summary>A store whose settings were changed by someone else between the read and the append.</summary>
    private sealed class RacingStore(FakeSizingStore inner) : XDrishti.Application.Abstractions.Selection.ISizingStore
    {
        public ValueTask<XDrishti.Application.Abstractions.Selection.SizingState> CurrentAsync(CancellationToken cancellationToken) => inner.CurrentAsync(cancellationToken);

        public ValueTask<IReadOnlyList<XDrishti.Application.Abstractions.Selection.SizingChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken) => inner.HistoryAsync(limit, cancellationToken);

        public ValueTask<IReadOnlyDictionary<Guid, double>> BudgetsAsync(CancellationToken cancellationToken) => inner.BudgetsAsync(cancellationToken);

        public ValueTask<IReadOnlyList<XDrishti.Application.Abstractions.Selection.BudgetChangeRow>> BudgetHistoryAsync(int limit, CancellationToken cancellationToken) => inner.BudgetHistoryAsync(limit, cancellationToken);

        public ValueTask<bool> AppendSettingsAsync(IReadOnlyList<XDrishti.Domain.Selection.SizeScaling.SizingChange> rows, long expectedVersion, CancellationToken cancellationToken) => ValueTask.FromResult(false);

        public ValueTask<bool> AppendBudgetAsync(Guid basketId, double? expectedCurrentPct, double? budgetPct, XDrishti.Domain.Selection.SizeScaling.SizingChange change, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}
