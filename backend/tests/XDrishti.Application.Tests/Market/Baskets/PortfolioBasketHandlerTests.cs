using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Market.Baskets;

/// <summary>Portfolio buckets as read-only system baskets (ADR 0014): listing, members, history, protection and copy-out.</summary>
public sealed class PortfolioBasketHandlerTests
{
    private readonly BasketFixture _f = new();

    private static CancellationToken Ct => BasketFixture.Ct;

    private static async Task<Error> SystemErrorAsync<T>(ValueTask<Result<T>> call)
    {
        var result = await call;
        result.IsFailure.ShouldBeTrue();
        return result.Error;
    }

    [Fact]
    public async Task The_list_puts_the_portfolio_baskets_first_then_the_manual_ones_in_their_order()
    {
        _f.Basket("Core");
        _f.Basket("Swing");
        var dhan = _f.Portfolio.Add("Dhan · Portfolio", "Dhan");

        var list = await _f.List.HandleAsync(new ListBasketsQuery(), Ct);

        list.Select(b => b.Name).ShouldBe(["Dhan · Portfolio", "Core", "Swing"]);
        list.Select(b => (b.Kind, b.System)).ShouldBe([("portfolio", true), ("manual", false), ("manual", false)]);
        list[0].Id.ShouldBe(dhan);
    }

    [Fact]
    public async Task Archived_manual_baskets_are_listed_on_request_after_the_portfolio_baskets()
    {
        var old = _f.Basket("Old");
        old.Archive(_f.Clock.GetUtcNow());
        _f.Portfolio.Add("Dhan · Portfolio", "Dhan");

        var list = await _f.List.HandleAsync(new ListBasketsQuery(IncludeArchived: true), Ct);

        list.Select(b => b.Name).ShouldBe(["Dhan · Portfolio", "Old"]);
    }

    [Fact]
    public async Task Members_of_a_portfolio_basket_come_from_the_holdings()
    {
        var id = _f.Portfolio.Add("Dhan · Portfolio", "Dhan", _f.Instruments("NSE:AAA", "NSE:BBB"));

        var result = await _f.Members.HandleAsync(new GetBasketMembersQuery(id), Ct);

        result.Value.Name.ShouldBe("Dhan · Portfolio");
        result.Value.Total.ShouldBe(2);
        _f.Data.LastMembersCall.ShouldNotBeNull().BasketId.ShouldBe(id);
    }

    [Fact]
    public async Task A_date_on_a_portfolio_basket_is_rejected()
    {
        var id = _f.Portfolio.Add("Dhan · Portfolio", "Dhan");

        var error = await SystemErrorAsync(_f.Members.HandleAsync(new GetBasketMembersQuery(id, "2026-10-11"), Ct));

        error.Code.ShouldBe("baskets.invalid");
        error.Fields!.Single().Field.ShouldBe("asOf");
    }

    [Fact]
    public async Task A_portfolio_basket_has_no_history()
    {
        var id = _f.Portfolio.Add("Dhan · Portfolio", "Dhan");

        var error = await SystemErrorAsync(_f.History.HandleAsync(new GetBasketHistoryQuery(id), Ct));

        error.Code.ShouldBe("baskets.invalid");
    }

    [Fact]
    public async Task Every_change_of_a_portfolio_basket_is_refused_as_a_system_basket()
    {
        var instruments = _f.Instruments("NSE:AAA");
        var id = _f.Portfolio.Add("Dhan · Portfolio", "Dhan", instruments);
        var saves = _f.Data.Saves;

        var errors = new[]
        {
            (await _f.Update.HandleAsync(new UpdateBasketCommand(id, new BasketInput("Renamed", null, null, null), BasketFixture.Owner), Ct)).Error,
            (await _f.Archive.HandleAsync(new ArchiveBasketCommand(id, BasketFixture.Owner), Ct)).Error,
            (await _f.Add.HandleAsync(new AddBasketMembersCommand(id, new AddMembersInput(instruments, null), BasketFixture.Owner), Ct)).Error,
            (await _f.Remove.HandleAsync(new RemoveBasketMembersCommand(id, new RemoveMembersInput(instruments), BasketFixture.Owner), Ct)).Error,
            (await _f.ReorderMembers.HandleAsync(new ReorderBasketMembersCommand(id, new MembersOrderInput(instruments), BasketFixture.Owner), Ct)).Error,
        };

        errors.ShouldAllBe(e => e.Code == "baskets.system" && e.Kind == ErrorKind.Conflict);
        _f.Data.Saves.ShouldBe(saves);
        _f.Master.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_id_is_still_not_found_on_the_change_routes()
    {
        var unknown = Guid.NewGuid();

        (await _f.Update.HandleAsync(new UpdateBasketCommand(unknown, new BasketInput("X", null, null, null), BasketFixture.Owner), Ct)).Error.Code.ShouldBe("baskets.not_found");
        (await _f.Archive.HandleAsync(new ArchiveBasketCommand(unknown, BasketFixture.Owner), Ct)).Error.Code.ShouldBe("baskets.not_found");
    }

    [Fact]
    public async Task Copying_from_a_portfolio_basket_adds_its_instruments_to_a_manual_basket_and_tracks_them_as_they_are_not_tracked()
    {
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB", "NSE:CCC");
        var source = _f.Portfolio.Add("Dhan · Portfolio", "Dhan", ids);
        var target = _f.Basket("Core");

        var result = await _f.Transfer.HandleAsync(new TransferBasketMembersCommand(new TransferInput(source, target.Id, [ids[2], ids[0]], "copy"), BasketFixture.Owner), Ct);

        result.Value.ShouldBe(new TransferResult("copy", 2, 2, 0, 0, 0, 2, 3, 2));
        _f.Data.ActiveInstruments(target).ShouldBe([ids[0], ids[2]], "the order of the holdings, not of the request");
        var audit = _f.Master.Events.Single(e => e.Event == XDrishti.Domain.Accounts.AccountEventType.BasketMembersTransferred);
        audit.Detail.ShouldNotBeNull().ShouldContain("from portfolio basket 'Dhan · Portfolio' to basket 'Core'");
    }

    [Fact]
    public async Task Copying_from_a_portfolio_basket_leaves_instruments_that_are_tracked_alone()
    {
        var ids = _f.Instruments("NSE:AAA");
        _f.Track(ids);
        var source = _f.Portfolio.Add("Dhan · Portfolio", "Dhan", ids);
        var target = _f.Basket("Core");

        var result = await _f.Transfer.HandleAsync(new TransferBasketMembersCommand(new TransferInput(source, target.Id, ids, "copy"), BasketFixture.Owner), Ct);

        result.Value.NewlyTracked.ShouldBe(0);
        result.Value.Added.ShouldBe(1);
    }

    [Fact]
    public async Task Instruments_that_the_portfolio_basket_does_not_hold_are_skipped()
    {
        var held = _f.Instruments("NSE:AAA");
        var stranger = _f.Instruments("NSE:OTHER");
        var source = _f.Portfolio.Add("Dhan · Portfolio", "Dhan", held);
        var target = _f.Basket("Core");

        var result = await _f.Transfer.HandleAsync(new TransferBasketMembersCommand(new TransferInput(source, target.Id, [held[0], stranger[0]], "copy"), BasketFixture.Owner), Ct);

        result.Value.ShouldBe(new TransferResult("copy", 2, 1, 0, 0, 1, 1, 1, 1));
    }

    [Fact]
    public async Task Moving_from_a_portfolio_basket_and_copying_or_moving_into_one_are_refused()
    {
        var ids = _f.Instruments("NSE:AAA");
        var portfolio = _f.Portfolio.Add("Dhan · Portfolio", "Dhan", ids);
        var other = _f.Portfolio.Add("Custom", null, ids);
        var manual = _f.Basket("Core");
        await _f.AddOkAsync(manual, ids);
        var saves = _f.Data.Saves;

        foreach (var (from, to, mode) in new[] { (portfolio, manual.Id, "move"), (manual.Id, portfolio, "copy"), (manual.Id, portfolio, "move"), (portfolio, other, "copy") })
        {
            var result = await _f.Transfer.HandleAsync(new TransferBasketMembersCommand(new TransferInput(from, to, ids, mode), BasketFixture.Owner), Ct);

            result.Error.Code.ShouldBe("baskets.system");
        }

        _f.Data.Saves.ShouldBe(saves);
        _f.Data.ActiveInstruments(manual).ShouldBe(ids);
    }
}
