using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Model;

/// <summary>Criterion 2: the records compare by value, lists element by element (a C# record compares a list by reference, so the model writes its own equality).</summary>
public sealed class ModelEqualityTests
{
    /// <summary>A definition that uses every kind of field once: a setup, a trigger, an invalidate list, an overlay, a named quantity, a stop timeframe, a filter, time rules.</summary>
    private static StrategyDefinition Full() => new(
        1,
        "FULL",
        Style.Intraday,
        [Timeframe.M5, Timeframe.M15],
        [Side.Buy, Side.Sell],
        [new StrategyParam("a", 1), new StrategyParam("b", 2.5)],
        new SideRules(
            Defs.All("close > 1", "rsi(14) > a"),
            Defs.Any("close > open"),
            Defs.All("close < 0"),
            new EntrySpec(EntryKind.AtClose),
            new StopSpec(StopKind.SwingLow, Timeframe.M15, Lookback: Quantity.Named("a"), BufferAtr: Quantity.Of(0.5)),
            [Defs.FixedRr(1.5, 2), new ExitSpec(ExitPolicy.PartialTrail, [Defs.P("book_pct", Quantity.Named("b")), Defs.P("t1_rr", 1), Defs.P("trail_atr_mult", 2)])],
            [new OverlaySpec(OverlayKind.TimeStop, [Defs.P("intraday_bars", 12)])]),
        Defs.SimpleRules(Side.Sell),
        new TimeRules(new TimeOnly(9, 30), new TimeOnly(11, 30), new TimeOnly(15, 15), null),
        [FilterNames.NotEventDay]);

    [Fact]
    public void Two_definitions_built_twice_from_the_same_data_are_equal_and_have_the_same_hash_code()
    {
        var first = Full();
        var second = Full();

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
        first.Equals(second).ShouldBeTrue();
        (first == second).ShouldBeTrue();
        Defs.VwapRsiTrend().ShouldBe(Defs.VwapRsiTrend());
        Defs.VwapRsiTrend().GetHashCode().ShouldBe(Defs.VwapRsiTrend().GetHashCode());
    }

    public static TheoryData<string, Func<StrategyDefinition, StrategyDefinition>> Changes { get; } = new()
    {
        { "an item text", d => d.WithBuy(b => b with { Trigger = Defs.Any("close > high") }) },
        { "an item text in a list", d => d.WithBuy(b => b with { Setup = Defs.All("close > 1", "rsi(14) > b") }) },
        { "the mode of a list", d => d.WithBuy(b => b with { Setup = Defs.Any("close > 1", "rsi(14) > a") }) },
        { "a param value", d => d with { Params = [new StrategyParam("a", 1), new StrategyParam("b", 2.6)] } },
        { "a param name", d => d with { Params = [new StrategyParam("a", 1), new StrategyParam("c", 2.5)] } },
        { "an exit value", d => d.WithBuy(b => b with { Exits = [Defs.FixedRr(1.5, 2.5), b.Exits![1]] }) },
        { "an exit policy", d => d.WithBuy(b => b with { Exits = [b.Exits![0]] }) },
        { "a named quantity", d => d.WithBuy(b => b with { Stop = b.Stop! with { Lookback = Quantity.Named("b") } }) },
        { "a literal quantity in place of a named one", d => d.WithBuy(b => b with { Stop = b.Stop! with { Lookback = Quantity.Of(1) } }) },
        { "a nullable field set", d => d.WithBuy(b => b with { Stop = b.Stop! with { Pct = Quantity.Of(1) } }) },
        { "a stop timeframe", d => d.WithBuy(b => b with { Stop = b.Stop! with { Timeframe = null } }) },
        { "an overlay", d => d.WithBuy(b => b with { Overlays = null }) },
        { "an overlay value", d => d.WithBuy(b => b with { Overlays = [new OverlaySpec(OverlayKind.TimeStop, [Defs.P("intraday_bars", 13)])] }) },
        { "an invalidate list", d => d.WithBuy(b => b with { Invalidate = null }) },
        { "a time rule", d => d with { TimeRules = d.TimeRules! with { LastEntry = new TimeOnly(11, 31) } } },
        { "a missing time rule", d => d with { TimeRules = d.TimeRules! with { FirstEntry = null } } },
        { "the time rules", d => d with { TimeRules = null } },
        { "a filter", d => d with { Filters = [FilterNames.MinTurnover] } },
        { "a timeframe", d => d with { Timeframes = [Timeframe.M5, Timeframe.M30] } },
        { "a side", d => d with { Sides = [Side.Sell, Side.Buy] } },
        { "the id", d => d with { Id = "OTHER" } },
        { "the style", d => d with { Style = Style.Swing } },
        { "the sell block", d => d with { Sell = null } },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Changing_one_thing_makes_the_definitions_unequal(string what, Func<StrategyDefinition, StrategyDefinition> change)
    {
        var original = Full();

        var changed = change(Full());

        changed.ShouldNotBe(original, what);
        changed.Equals(original).ShouldBeFalse(what);
    }

    [Fact]
    public void A_null_list_is_not_an_empty_list()
    {
        var withNone = Full().WithBuy(b => b with { Overlays = null });
        var withEmpty = Full().WithBuy(b => b with { Overlays = [] });

        withNone.ShouldNotBe(withEmpty);
    }

    [Fact]
    public void A_quantity_compares_by_number_and_by_parameter_name()
    {
        Quantity.Of(2).ShouldBe(Quantity.Of(2));
        Quantity.Named("x").ShouldBe(Quantity.Named("x"));
        Quantity.Of(2).ShouldNotBe(Quantity.Of(3));
        Quantity.Of(2).ShouldNotBe(Quantity.Named("x"));
        Quantity.Of(2).GetHashCode().ShouldBe(Quantity.Of(2).GetHashCode());
    }

    [Fact]
    public void The_exit_parameters_and_condition_lists_compare_their_values_element_by_element()
    {
        Defs.P("rr", 1.0, 2.0).ShouldBe(Defs.P("rr", 1.0, 2.0));
        Defs.P("rr", 1.0, 2.0).ShouldNotBe(Defs.P("rr", 2.0, 1.0));
        Defs.All("a > 1", "b > 2").ShouldBe(Defs.All("a > 1", "b > 2"));
        Defs.All("a > 1", "b > 2").GetHashCode().ShouldBe(Defs.All("a > 1", "b > 2").GetHashCode());
        Defs.All("a > 1").ShouldNotBe(Defs.All("a > 1", "b > 2"));
    }
}
