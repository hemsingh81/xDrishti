using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets.Derived;

public sealed class BackfillAndParamsHandlerTests
{
    private static readonly DateOnly From = new(2026, 9, 14);
    private static readonly DateOnly To = new(2026, 9, 25);

    [Fact]
    public async Task A_backfill_writes_one_recomputed_version_per_due_date_with_closed_windows()
    {
        var world = DerivedWorld.WithLiquidBasket(12, bars: 140);

        var result = await world.Backfill().HandleAsync(new BackfillDerivedBasketCommand(world.Liquid.BasketId, From, To, "owner"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        world.Store.Versions.Count.ShouldBe(10); // ten weekdays, a daily basket
        world.Store.Versions.ShouldAllBe(v => v.Provenance == BasketProvenance.Recomputed);
        world.Store.Versions.Select(v => v.AsOf).ShouldBe(Enumerable.Range(0, 12).Select(i => From.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)));
        world.Store.Versions.ShouldAllBe(v => v.ValidTo != null && v.ValidTo > v.EffectiveFrom);
        world.Store.Versions[0].ValidTo.ShouldBe(world.Store.Versions[1].EffectiveFrom); // one window ends where the next version starts
        result.Value.Note.ShouldNotBeNull().ShouldContain("restated");
    }

    [Fact]
    public async Task A_backfill_never_overwrites_a_version_that_exists()
    {
        var world = DerivedWorld.WithLiquidBasket(12, bars: 140);
        var command = new BackfillDerivedBasketCommand(world.Liquid.BasketId, From, To, "owner");
        await world.Backfill().HandleAsync(command, TestContext.Current.CancellationToken);

        var again = await world.Backfill().HandleAsync(command, TestContext.Current.CancellationToken);

        world.Store.Versions.Count.ShouldBe(10);
        again.Value.Summary.ShouldContain("0 recomputed");
    }

    [Fact]
    public async Task A_backfill_under_another_parameter_set_over_recomputed_dates_is_a_conflict()
    {
        var world = DerivedWorld.WithLiquidBasket(12, bars: 140);
        await world.Backfill().HandleAsync(new BackfillDerivedBasketCommand(world.Liquid.BasketId, From, To, "owner"), TestContext.Current.CancellationToken);
        await world.SetParams().HandleAsync(new SetDerivedParamsCommand(world.Liquid.BasketId, new Dictionary<string, double> { ["Basket3.TopK"] = 5 }, null, "owner"), TestContext.Current.CancellationToken);

        var result = await world.Backfill().HandleAsync(new BackfillDerivedBasketCommand(world.Liquid.BasketId, From, To, "owner"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(BasketErrors.BackfillOverlapCode);
        world.Store.Versions.Count.ShouldBe(10);
    }

    [Fact]
    public async Task A_backfill_that_reaches_the_first_published_version_is_a_conflict()
    {
        var world = DerivedWorld.WithLiquidBasket(12, bars: 140);
        await world.Refresh().HandleAsync(new RefreshDerivedBasketsCommand("system:test", false, new DateOnly(2026, 9, 28)), TestContext.Current.CancellationToken); // published, effective 2026-09-29

        var result = await world.Backfill().HandleAsync(new BackfillDerivedBasketCommand(world.Liquid.BasketId, From, new DateOnly(2026, 9, 29), "owner"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(BasketErrors.BackfillOverlapCode);
    }

    [Fact]
    public async Task A_backfill_longer_than_the_limit_is_refused_naming_the_limit()
    {
        var world = DerivedWorld.WithLiquidBasket(5, bars: 140);
        world.Settings.MaxBackfillDays = 5;

        var result = await world.Backfill().HandleAsync(new BackfillDerivedBasketCommand(world.Liquid.BasketId, From, To, "owner"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(BasketErrors.InvalidCode);
        result.Error.Fields.ShouldNotBeNull().ShouldContain(f => f.Message.Contains('5', StringComparison.Ordinal));
        world.Store.Versions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_backfill_is_refused_while_the_setting_is_off()
    {
        var world = DerivedWorld.WithLiquidBasket(5, bars: 140);
        world.Settings.Enabled = false;

        var result = await world.Backfill().HandleAsync(new BackfillDerivedBasketCommand(world.Liquid.BasketId, From, To, "owner"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(BasketErrors.DerivedDisabledCode);
        world.Universe.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task A_new_parameter_set_is_validated_against_the_engines_ranges_and_never_edits_an_old_one()
    {
        var world = DerivedWorld.WithLiquidBasket(5);

        var bad = await world.SetParams().HandleAsync(new SetDerivedParamsCommand(world.Liquid.BasketId, new Dictionary<string, double> { ["Basket3.TopK"] = -4 }, null, "owner"), TestContext.Current.CancellationToken);
        var unknown = await world.SetParams().HandleAsync(new SetDerivedParamsCommand(world.Liquid.BasketId, new Dictionary<string, double> { ["Nope"] = 1 }, null, "owner"), TestContext.Current.CancellationToken);
        var first = await world.SetParams().HandleAsync(new SetDerivedParamsCommand(world.Liquid.BasketId, new Dictionary<string, double> { ["Basket3.TopK"] = 40 }, null, "owner"), TestContext.Current.CancellationToken);
        var again = await world.SetParams().HandleAsync(new SetDerivedParamsCommand(world.Liquid.BasketId, new Dictionary<string, double> { ["Basket3.TopK"] = 40 }, null, "owner"), TestContext.Current.CancellationToken);

        bad.Error.Code.ShouldBe(BasketErrors.InvalidCode);
        unknown.Error.Code.ShouldBe(BasketErrors.InvalidCode);
        first.IsSuccess.ShouldBeTrue();
        again.Value.ParamsHash.ShouldBe(first.Value.ParamsHash);
        world.Store.ParamSets.ShouldHaveSingleItem(); // the same numbers make no new set
    }

    [Theory]
    [InlineData(13)]
    [InlineData(4)]
    [InlineData(12)]
    public async Task A_basket_this_build_cannot_form_cannot_be_switched_on(int definitionNo)
    {
        var world = DerivedWorld.WithLiquidBasket(1);
        var basket = new DerivedDefinitionRow(Guid.NewGuid(), "x", definitionNo, false, null, new Dictionary<string, double>());
        world.Store.Definitions.Add(basket);

        var result = await world.SetParams().HandleAsync(new SetDerivedParamsCommand(basket.BasketId, null, true, "owner"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(BasketErrors.InvalidCode);
        world.Store.Definitions.Single(d => d.BasketId == basket.BasketId).Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Switching_a_formable_basket_on_is_stored()
    {
        var world = DerivedWorld.WithLiquidBasket(1, enabled: false);

        var result = await world.SetParams().HandleAsync(new SetDerivedParamsCommand(world.Liquid.BasketId, null, true, "owner"), TestContext.Current.CancellationToken);

        result.Value.Enabled.ShouldBeTrue();
    }
}
