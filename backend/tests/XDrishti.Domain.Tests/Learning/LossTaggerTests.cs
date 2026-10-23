using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>One fixture per loss tag, the honest untagged outcome, and the fact that a tag reads nothing but the closed trade (P6-15, ADR 0071).</summary>
public sealed class LossTaggerTests
{
    private static readonly LossTagOptions Options = new();

    /// <summary>A stop-out of exactly one risk, held an hour, decided at 10:00, 1 per cent stop, no slippage: tags to normal variance and nothing else.</summary>
    private static SimTrade Plain(Side side = Side.Buy)
    {
        var buy = side == Side.Buy;
        var t = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", side, FabricatedRun.Monday, ExitReason.Stop, entryMinute: 600, entry: 100, stop: buy ? 99 : 101, target: buy ? 102 : 98, exitPrice: buy ? 99 : 101, rExact: -1, rFilled: -1);
        return t with { MaeR = 1, MfeR = 0.2, ExitEpochSeconds = t.EntryEpochSeconds + 3600 };
    }

    private static LossTagKind[] Kinds(SimTrade t) => [.. LossTagger.Tag(t, Options).Select(x => x.Kind)];

    [Fact]
    public void A_plain_stop_out_is_normal_variance_and_nothing_else()
    {
        var tags = LossTagger.Tag(Plain(), Options);

        var tag = tags.ShouldHaveSingleItem();
        tag.Kind.ShouldBe(LossTagKind.NormalVariance);
        tag.Value.ShouldBe(-1);
        tag.Detail.ShouldContain("-1.00 R");
    }

    [Theory]
    [InlineData(Side.Buy, 98.5, true)]
    [InlineData(Side.Sell, 101.5, true)]
    [InlineData(Side.Buy, 98.95, false)]
    [InlineData(Side.Buy, 99, false)]
    public void Gap_through_the_stop_needs_the_exit_beyond_the_stop_by_the_threshold(Side side, double exit, bool tagged)
    {
        var t = Plain(side) with { ExitPrice = exit };

        var tag = LossTagger.Tag(t, Options).FirstOrDefault(x => x.Kind == LossTagKind.GapThroughStop);

        (tag is not null).ShouldBe(tagged);
        if (tag is not null)
        {
            tag.Value.ShouldBe(0.5, 1e-9);
            tag.Detail.ShouldContain("0.50 R beyond");
        }
    }

    [Fact]
    public void A_gap_is_only_read_from_a_stop_exit()
    {
        Kinds(Plain() with { ExitReason = ExitReason.SquareOff, ExitPrice = 98 }).ShouldNotContain(LossTagKind.GapThroughStop);
    }

    [Theory]
    [InlineData(1.4, true)]
    [InlineData(1.0, true)]
    [InlineData(0.99, false)]
    public void Gave_back_profit_needs_the_favourable_excursion_at_the_threshold(double mfe, bool tagged)
    {
        var tag = LossTagger.Tag(Plain() with { MfeR = mfe }, Options).FirstOrDefault(x => x.Kind == LossTagKind.GaveBackProfit);

        (tag is not null).ShouldBe(tagged);
        if (tag is not null)
        {
            tag.Value.ShouldBe(mfe);
            tag.Detail.ShouldContain("ended -1.00 R");
        }
    }

    [Fact]
    public void High_slippage_needs_modelled_fills_that_cost_the_threshold()
    {
        var slipped = Plain() with { RFilled = -1.2, SlippageR = -0.2 };

        var tag = LossTagger.Tag(slipped, Options).Single(x => x.Kind == LossTagKind.HighSlippage);

        tag.Value.ShouldBe(0.2);
        Kinds(slipped with { SlippageR = -0.1 }).ShouldNotContain(LossTagKind.HighSlippage);
        Kinds(slipped with { Fills = FillBasis.Exact }).ShouldNotContain(LossTagKind.HighSlippage);
    }

    [Fact]
    public void Late_entry_is_a_decision_from_the_cut_off_of_a_trade_that_left_on_its_entry_date()
    {
        var late = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, FabricatedRun.Monday, ExitReason.SquareOff, entryMinute: 880, entry: 100, stop: 99, target: null, exitPrice: 99.5, rExact: -0.5, rFilled: -0.5) with { MfeR = 0.1 };

        var tag = LossTagger.Tag(late, Options).Single(x => x.Kind == LossTagKind.LateEntry);

        tag.Value.ShouldBe(878);
        tag.Detail.ShouldContain("14:38");
        Kinds(late with { SessionsHeld = 2 }).ShouldNotContain(LossTagKind.LateEntry);
        Kinds(late with { EntryEpochSeconds = late.EntryEpochSeconds + 86400 }).ShouldNotContain(LossTagKind.LateEntry);
        var early = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, FabricatedRun.Monday, ExitReason.SquareOff, entryMinute: 870, entry: 100, stop: 99, target: null, exitPrice: 99.5, rExact: -0.5, rFilled: -0.5);
        Kinds(early).ShouldNotContain(LossTagKind.LateEntry);
    }

    [Theory]
    [InlineData(869, false)]
    [InlineData(870, true)]
    public void Late_entry_uses_the_minute_that_closed_the_bar_as_the_decision_minute(int closingMinute, bool tagged)
    {
        var t = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, FabricatedRun.Monday, ExitReason.SquareOff, entryMinute: 875, entry: 100, stop: 99, target: null, exitPrice: 99.5, rExact: -0.5, rFilled: -0.5) with { MfeR = 0.1 };
        // The bar of closingMinute ends one minute later: that instant is the decision.
        var decided = t with { DecisionEpochSeconds = Day.Epoch(FabricatedRun.Monday, closingMinute + 1) };

        Kinds(decided).Contains(LossTagKind.LateEntry).ShouldBe(tagged);
    }

    [Fact]
    public void A_stop_trailed_above_the_initial_stop_is_not_a_gap()
    {
        // Sold at the trailed level 99.5, above the initial stop 99: the gap rule compares with the initial StopPrice, so nothing is beyond it.
        Kinds(Plain() with { ExitPrice = 99.5 }).ShouldNotContain(LossTagKind.GapThroughStop);
    }

    [Fact]
    public void A_stop_out_that_loses_only_the_costs_is_not_normal_variance()
    {
        Kinds(Plain() with { RExact = 0.05, RFilled = -0.02 }).ShouldNotContain(LossTagKind.NormalVariance);
    }

    [Theory]
    [InlineData(5, 0.2, true)]
    [InlineData(10, 0.2, true)]
    [InlineData(11, 0.2, false)]
    [InlineData(5, 0.6, false)]
    public void A_quick_stop_is_a_fast_stop_exit_with_almost_no_favourable_move(int minutes, double mfe, bool tagged)
    {
        var t = Plain();
        var quick = t with { ExitEpochSeconds = t.EntryEpochSeconds + (minutes * 60L), MfeR = mfe };

        Kinds(quick).Contains(LossTagKind.QuickStop).ShouldBe(tagged);
    }

    [Fact]
    public void A_wide_stop_is_a_stop_distance_of_a_large_share_of_the_price()
    {
        var wide = Plain() with { StopPrice = 98 };

        var tag = LossTagger.Tag(wide, Options).Single(x => x.Kind == LossTagKind.WideStop);

        tag.Value.ShouldBe(0.02, 1e-12);
        Kinds(Plain() with { StopPrice = 98.4 }).ShouldContain(LossTagKind.WideStop);
        Kinds(Plain() with { StopPrice = 98.6 }).ShouldNotContain(LossTagKind.WideStop);
    }

    [Fact]
    public void A_loss_that_fits_no_rule_is_untagged()
    {
        var t = Plain() with { ExitReason = ExitReason.SquareOff, ExitPrice = 98.7, RExact = -1.3, RFilled = -1.3 };

        LossTagger.Tag(t, Options).ShouldBeEmpty();
    }

    [Fact]
    public void A_stop_out_beyond_the_normal_band_is_not_normal_variance()
    {
        Kinds(Plain() with { RExact = -1.5 }).ShouldNotContain(LossTagKind.NormalVariance);
    }

    [Fact]
    public void Several_tags_come_in_the_fixed_order_and_normal_variance_steps_aside()
    {
        var t = Plain() with { ExitPrice = 98.5, MfeR = 1.5, StopPrice = 99, SlippageR = -0.3, RFilled = -1.3 };
        t = t with { ExitEpochSeconds = t.EntryEpochSeconds + 120 };

        Kinds(t).ShouldBe([LossTagKind.GapThroughStop, LossTagKind.GaveBackProfit, LossTagKind.HighSlippage]);
    }

    [Fact]
    public void Tags_read_only_the_closed_trade_not_who_or_what_surrounds_it()
    {
        var a = Plain();
        var b = a with { StrategyId = "OTHER", Symbol = "NSE:Z", InstrumentId = Guid.NewGuid(), EntrySnapshotJson = "{}", TriggerBarMinutes = 5, TickFallback = true };

        LossTagger.Tag(b, Options).ShouldBe(LossTagger.Tag(a, Options));
    }

    [Fact]
    public void A_missing_argument_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => LossTagger.Tag(null!, Options));
        Should.Throw<ArgumentNullException>(() => LossTagger.Tag(Plain(), null!));
    }
}
