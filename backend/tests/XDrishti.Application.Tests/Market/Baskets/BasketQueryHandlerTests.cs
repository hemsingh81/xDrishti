using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Market.Baskets;

public sealed class BasketQueryHandlerTests
{
    private readonly BasketFixture _f = new();

    private static CancellationToken Ct => BasketFixture.Ct;

    [Fact]
    public async Task The_as_of_date_becomes_the_cutoff_after_that_day_in_India()
    {
        var basket = _f.Basket("Core");

        var result = await _f.Members.HandleAsync(new GetBasketMembersQuery(basket.Id, " 2026-10-11 "), Ct);

        result.IsSuccess.ShouldBeTrue();
        _f.Data.LastMembersCall.ShouldBe((basket.Id, new DateTimeOffset(2026, 10, 11, 18, 30, 0, TimeSpan.Zero), new DateOnly(2026, 10, 11)));
    }

    [Fact]
    public async Task Without_an_as_of_date_the_current_members_are_read()
    {
        var basket = _f.Basket("Core");

        await _f.Members.HandleAsync(new GetBasketMembersQuery(basket.Id), Ct);
        await _f.Members.HandleAsync(new GetBasketMembersQuery(basket.Id, "  "), Ct);

        _f.Data.LastMembersCall.ShouldBe((basket.Id, null, null));
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-13-01")]
    [InlineData("11/10/2026")]
    [InlineData("2026-10-11T10:00:00Z")]
    [InlineData("1999-12-31")]
    [InlineData("3000-01-01")]
    public async Task A_bad_as_of_date_is_rejected(string asOf)
    {
        var basket = _f.Basket("Core");

        var result = await _f.Members.HandleAsync(new GetBasketMembersQuery(basket.Id, asOf), Ct);

        result.Error.Code.ShouldBe("baskets.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("asOf");
        _f.Data.LastMembersCall.ShouldBeNull();
    }

    [Fact]
    public async Task Members_and_history_of_an_unknown_basket_are_not_found()
    {
        (await _f.Members.HandleAsync(new GetBasketMembersQuery(Guid.NewGuid()), Ct)).Error.Code.ShouldBe("baskets.not_found");
        (await _f.History.HandleAsync(new GetBasketHistoryQuery(Guid.NewGuid()), Ct)).Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task The_history_take_defaults_to_two_hundred_and_must_be_between_one_and_a_thousand()
    {
        var basket = _f.Basket("Core");

        await _f.History.HandleAsync(new GetBasketHistoryQuery(basket.Id), Ct);
        _f.Data.LastHistoryCall.ShouldBe((basket.Id, 200));
        await _f.History.HandleAsync(new GetBasketHistoryQuery(basket.Id, 1000), Ct);
        _f.Data.LastHistoryCall.ShouldBe((basket.Id, 1000));

        foreach (var take in new[] { 0, -1, 1001 })
        {
            var result = await _f.History.HandleAsync(new GetBasketHistoryQuery(basket.Id, take), Ct);
            result.Error.Fields!.Single().Field.ShouldBe("take");
        }
    }

    [Fact]
    public async Task The_baskets_of_an_instrument_are_those_that_hold_it_now_and_are_not_archived()
    {
        var a = _f.Basket("A");
        var b = _f.Basket("B");
        var archived = _f.Basket("C");
        var id = _f.Instruments("NSE:AAA")[0];
        _f.Data.AddMember(a, id, _f.Clock.GetUtcNow());
        _f.Data.AddMember(b, id, _f.Clock.GetUtcNow()).Remove("o", _f.Clock.GetUtcNow());
        _f.Data.AddMember(archived, id, _f.Clock.GetUtcNow());
        archived.Archive(_f.Clock.GetUtcNow());

        var result = await _f.ForInstrument.HandleAsync(new ListInstrumentBasketsQuery(id), Ct);

        result.Value.Select(x => x.Name).ShouldBe(["A"]);
        (await _f.ForInstrument.HandleAsync(new ListInstrumentBasketsQuery(Guid.NewGuid()), Ct)).Error.Code.ShouldBe("instruments.not_found");
    }
}
