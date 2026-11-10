using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets.Systems;

/// <summary>A stored index basket is read-only: every change route is 409 baskets.system; a copy out of it is allowed; it is never in a reorder.</summary>
public sealed class IndexBasketProtectionTests
{
    private readonly BasketFixture _f = new();

    private static CancellationToken Ct => BasketFixture.Ct;

    private Basket Index()
    {
        var basket = Basket.CreateSystem("NSE Nifty 50", BasketKinds.Index, _f.Clock.GetUtcNow());
        _f.Data.Baskets.Add(basket);
        return basket;
    }

    private static void IsSystemConflict(Result result)
    {
        result.IsFailure.ShouldBeTrue();
        (result.Error.Code, result.Error.Kind).ShouldBe(("baskets.system", ErrorKind.Conflict));
    }

    [Fact]
    public async Task Update_archive_and_the_member_routes_are_refused()
    {
        var index = Index();
        var ids = _f.Instruments("NSE:AAA");

        IsSystemConflict(await _f.Update.HandleAsync(new UpdateBasketCommand(index.Id, new BasketInput("Renamed", null, null, null), BasketFixture.Owner), Ct));
        IsSystemConflict(await _f.Archive.HandleAsync(new ArchiveBasketCommand(index.Id, BasketFixture.Owner), Ct));
        IsSystemConflict(await _f.AddAsync(index, ids));
        IsSystemConflict(await _f.RemoveAsync(index, ids));
        IsSystemConflict(await _f.ReorderMembersAsync(index, ids));
        index.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public async Task Transfer_into_it_or_a_move_out_of_it_is_refused_but_a_copy_out_is_allowed()
    {
        var index = Index();
        var manual = _f.Basket("Mine");
        var ids = _f.Instruments("NSE:AAA", "NSE:BBB");
        _f.Data.AddMember(index, ids[0], _f.Clock.GetUtcNow());

        IsSystemConflict(await _f.TransferAsync(manual, index, "copy", ids));
        IsSystemConflict(await _f.TransferAsync(index, manual, "move", ids));
        var copy = await _f.TransferAsync(index, manual, "copy", ids[0]);

        copy.IsSuccess.ShouldBeTrue();
        copy.Value.Added.ShouldBe(1);
        _f.Data.Members.Count(m => m.BasketId == index.Id && m.IsActive).ShouldBe(1);
    }

    [Fact]
    public async Task A_reorder_of_the_manual_baskets_does_not_list_the_index_basket()
    {
        Index();
        var a = _f.Basket("A");
        var b = _f.Basket("B");

        var result = await _f.ReorderBaskets.HandleAsync(new ReorderBasketsCommand(new BasketOrderInput([b.Id, a.Id]), BasketFixture.Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        (b.Position, a.Position).ShouldBe((0, 1));
    }

    [Fact]
    public async Task A_new_manual_basket_is_placed_after_the_manual_ones_not_after_the_system_ones()
    {
        Index();
        var created = await _f.Create.HandleAsync(new CreateBasketCommand(new BasketInput("First", null, null, null), BasketFixture.Owner), Ct);

        created.Value.Position.ShouldBe(0);
    }
}
