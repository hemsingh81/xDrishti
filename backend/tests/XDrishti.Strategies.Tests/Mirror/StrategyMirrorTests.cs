using System.Diagnostics;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Mirror;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Mirror;

/// <summary>Spec P3-02e criteria 7, 8 and 11 and the side rules: expansion of a definition without a sell block, the version in the canonical text, the stop kinds, the cases left alone, and the speed.</summary>
public sealed class StrategyMirrorTests
{
    private static StrategyDefinition Mirrored() => Defs.VwapRsiTrend();

    [Fact]
    public void A_definition_with_sell_listed_and_no_sell_block_gets_the_mirror_of_its_buy_side_and_one_applied_note()
    {
        var (expanded, problems) = StrategyMirror.Expand(Mirrored());

        var sell = expanded.Sell.ShouldNotBeNull();
        sell.Setup!.Items.ShouldBe(["time >= \"09:45\"", "tf(\"1h\").ema(20) < tf(\"1h\").ema(50)", "rsi(rsi_len) < 100 - rsi_min"]);
        sell.Trigger!.Items.ShouldBe(["crosses_below(close, vwap)"]);
        sell.Stop!.Kind.ShouldBe(StopKind.SwingHigh);
        sell.Stop.Lookback.ShouldBe(Quantity.Named("swing_lookback"));
        sell.Exits.ShouldBe(Mirrored().Buy!.Exits);
        expanded.SellMirroredWith.ShouldBe(MirrorTable.Version);
        var note = problems.ShouldHaveSingleItem();
        note.Path.ShouldBe("sell");
        note.Problem.Code.ShouldBe(MirrorCodes.Applied);
        note.Problem.Severity.ShouldBe(ProblemSeverity.Information);
        note.Problem.Message.ShouldContain("crosses_below(close, vwap)");
    }

    [Fact]
    public void The_printer_given_by_the_caller_writes_the_block_in_the_note()
    {
        var (_, problems) = StrategyMirror.Expand(Mirrored(), rules => "BLOCK " + rules.Stop!.Kind);

        problems.Single().Problem.Message.ShouldEndWith("BLOCK SwingHigh");
    }

    [Fact]
    public void A_hand_written_sell_block_and_a_buy_only_strategy_are_left_alone()
    {
        var hand = Defs.Basic();
        var buyOnly = Mirrored() with { Sides = [Side.Buy] };
        var swing = Defs.Swing() with { Sides = [Side.Buy, Side.Sell] };

        StrategyMirror.Expand(hand).Definition.ShouldBeSameAs(hand);
        StrategyMirror.Expand(buyOnly).Definition.Sell.ShouldBeNull();
        StrategyMirror.Expand(swing).Definition.Sell.ShouldBeNull();
        StrategyMirror.Expand(Mirrored() with { Buy = null }).Problems.ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_condition_returns_the_definition_unchanged_with_an_error_at_the_sell_path()
    {
        var definition = Mirrored().WithBuy(b => b with { Trigger = Defs.All("crosses_above(close, vwap)", "vix > 20") });

        var (expanded, problems) = StrategyMirror.Expand(definition);

        expanded.Sell.ShouldBeNull();
        var error = problems.ShouldHaveSingleItem();
        error.Path.ShouldBe("sell.trigger[1]");
        error.Problem.Code.ShouldBe(MirrorCodes.ContextName);
    }

    [Theory]
    [InlineData(EntryKind.StopAboveHigh)]
    [InlineData(EntryKind.LimitPullback)]
    public void A_direction_specific_entry_kind_is_refused_at_sell_entry(EntryKind kind)
    {
        var definition = Mirrored().WithBuy(b => b with { Entry = new EntrySpec(kind) });

        var (expanded, problems) = StrategyMirror.Expand(definition);

        expanded.Sell.ShouldBeNull();
        problems.Single().Path.ShouldBe("sell.entry");
        problems.Single().Problem.Code.ShouldBe(MirrorCodes.NotMirrorable);
    }

    [Fact]
    public void A_buy_condition_that_does_not_compile_reports_no_mirror_problem()
    {
        var definition = Mirrored().WithBuy(b => b with { Trigger = Defs.All("closee > ") });

        var (expanded, problems) = StrategyMirror.Expand(definition);

        problems.Single().Problem.Code.ShouldBe(MirrorCodes.Applied);
        expanded.Sell!.Trigger!.Items.ShouldBe(["closee > "]);
    }

    [Theory]
    [InlineData(StopKind.SwingLow, StopKind.SwingHigh)]
    [InlineData(StopKind.BarLow, StopKind.BarHigh)]
    [InlineData(StopKind.FixedPct, StopKind.FixedPct)]
    [InlineData(StopKind.AtrMult, StopKind.AtrMult)]
    [InlineData(StopKind.OrOpposite, StopKind.OrOpposite)]
    public void The_stop_kind_swaps_for_the_low_and_high_kinds_and_every_field_is_kept(StopKind buy, StopKind sell)
    {
        var stop = new StopSpec(buy, Timeframe.M15, Quantity.Of(1), Quantity.Of(2), Quantity.Named("n"), Quantity.Of(15), Quantity.Of(0.1), Quantity.Of(14));
        var definition = Mirrored().WithBuy(b => b with { Stop = stop });

        var mirrored = StrategyMirror.Expand(definition).Definition.Sell!.Stop;

        mirrored.ShouldBe(stop with { Kind = sell });
    }

    [Fact]
    public void The_mirrored_sell_changes_the_definition_hash_with_the_table_version_and_the_buy_side_alone_does_not()
    {
        var expanded = StrategyMirror.Expand(Mirrored()).Definition;

        var json = CanonicalStrategy.Json(expanded);
        var otherVersion = CanonicalStrategy.Json(expanded with { SellMirroredWith = "mirror-2" });

        json.ShouldContain("\"sell_mirrored\":\"mirror-1\"");
        CanonicalText.Sha256Hex(json).ShouldNotBe(CanonicalText.Sha256Hex(otherVersion));
        CanonicalStrategy.Json(Mirrored()).ShouldNotContain("sell_mirrored");
    }

    [Fact]
    public void The_compiler_compiles_the_sell_side_of_a_definition_that_reaches_it_without_a_sell_block()
    {
        var result = Located.Compile(Mirrored(), Timeframe.M15, Side.Sell);

        result.IsSuccess.ShouldBeTrue(Located.Show(result));
    }

    [Fact]
    public void The_compiler_reports_a_refusal_of_the_mirror_for_the_sell_side()
    {
        var definition = Mirrored().WithBuy(b => b with { Trigger = Defs.All("close > 100") });

        var result = Located.Compile(definition, Timeframe.M15, Side.Sell);

        result.ShouldHave(MirrorCodes.AbsoluteLevel, "sell.trigger[0]");
    }

    [Fact]
    public void Mirroring_a_side_of_twenty_conditions_takes_under_twenty_milliseconds_warm()
    {
        var items = Enumerable.Range(0, 20).Select(i => i % 2 == 0 ? "close > ema(" + (10 + i) + ")" : "rsi(14) > " + (50 + i)).ToArray();
        var side = Mirrored().Buy! with { Setup = null, Trigger = Defs.All(items), Invalidate = null };
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 100; i++)
        {
            SideMirror.Mirror(side, names);
        }

        // the best of several rounds: a loaded machine slows a round, it cannot speed one up
        var best = double.MaxValue;
        for (var round = 0; round < 10; round++)
        {
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++)
            {
                SideMirror.Mirror(side, names);
            }

            best = Math.Min(best, clock.Elapsed.TotalMilliseconds / 20);
        }

        best.ShouldBeLessThan(20); // 2.2 ms alone, 5.2 ms when the machine is busy; this guards a regression of an order of magnitude, not a budget
    }
}
