using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Tests.Support;
using XDrishti.SimExport.Verification;

namespace XDrishti.SimExport.Tests.Verification;

/// <summary>Criterion 4 of P3-09: the edge table, one hand-made trade per category, the boundaries, and the enums the table is iterated from.</summary>
public sealed class EdgeCategoriesTests
{
    private static readonly DateOnly TradeDay = new(2025, 4, 8);

    private const string Orb15 = "{\"id\":\"ORB\",\"time_rules\":{\"first_entry\":\"09:30\",\"last_entry\":\"11:30\",\"square_off\":\"15:15\"}}";

    private const string Orb30 = "{\"id\":\"ORB\",\"time_rules\":{\"first_entry\":\"09:45\"}}";

    private static SimTrade Base(Side side = Side.Buy) => PackFixtures.Neutral("ORB", "NSE:E", TradeDay, side);

    private static IReadOnlyList<string> Of(SimTrade trade, string? definition = null, Func<InstrumentRun, InstrumentRun>? instrument = null)
    {
        var run = PackFixtures.Run("ORB", [trade], null, instrument, definition ?? Orb15);
        return EdgeCategories.Of(run, run.Trades[0]);
    }

    public static TheoryData<string, SimTrade, string?> OneTradePerCategory()
    {
        var sell = Base(Side.Sell);
        var gapBuy = Base() with { ExitPrice = 97.5 };
        var entry = Base().EntryEpochSeconds;
        return new TheoryData<string, SimTrade, string?>
        {
            { "sell", sell, null },
            { "stop_gap", gapBuy, null },
            { "stop_in_entry_minute", Base() with { ExitEpochSeconds = entry }, null },
            { "entry_at_limit", Base() with { EntryFill = EntryFill.AtLimit }, null },
            { "entry_after_hole", Base() with { DecisionEpochSeconds = entry - 120 }, null },
            { "first_evaluation", Base() with { DecisionEpochSeconds = Day9(570) }, null },
            { "overnight_entry", Base() with { DecisionEpochSeconds = Day9(900) - 86400 }, null },
            { "exit_target", Base() with { ExitReason = ExitReason.Target, ExitPrice = 105.5 }, null },
            { "exit_square_off", Base() with { ExitReason = ExitReason.SquareOff, ExitPrice = 100 }, null },
            { "exit_max_hold", Base() with { ExitReason = ExitReason.MaxHold, ExitPrice = 100 }, null },
            { "incomplete_trigger_bar", Base() with { TriggerBarComplete = false }, null },
            { "tick_fallback", Base() with { TickFallback = true }, null },
        };
    }

    private static long Day9(int minute) => XDrishti.Tests.Shared.Day.Epoch(TradeDay, minute);

    [Theory]
    [MemberData(nameof(OneTradePerCategory))]
    public void A_hand_made_trade_belongs_to_the_category_it_was_made_for_and_the_neutral_trade_to_none(string code, SimTrade trade, string? definition)
    {
        var categories = Of(trade, definition);

        categories.ShouldContain(code);
        Of(Base()).ShouldBeEmpty();
    }

    [Fact]
    public void The_adjusted_instrument_category_needs_an_applied_action_with_an_ex_date_after_the_trade_date()
    {
        InstrumentRun Split(DateOnly ex, InstrumentRun i) => i with { Adjustments = [new AppliedAdjustment(ex, CorporateActionKind.Split, 1, 2, 0.5m, false)] };

        Of(Base(), instrument: i => Split(TradeDay.AddDays(1), i)).ShouldBe(["adjusted_instrument"]);
        Of(Base(), instrument: i => Split(TradeDay, i)).ShouldBeEmpty("an action ex-dated on the trade day does not scale the day");
        Of(Base(), instrument: i => Split(TradeDay.AddDays(-1), i)).ShouldBeEmpty();
    }

    [Fact]
    public void A_stop_exactly_at_the_stop_price_is_not_a_gap_and_a_sell_with_the_exit_above_the_stop_is()
    {
        Of(Base()).ShouldNotContain("stop_gap");
        Of(Base() with { ExitPrice = 98.0000001 }).ShouldNotContain("stop_gap", "inside the stop for a buy");
        Of(Base(Side.Sell) with { ExitPrice = 103.5 }).ShouldBe(["sell", "stop_gap"]);
        Of(Base(Side.Sell) with { ExitPrice = 102.5 }).ShouldBe(["sell"]);
        Of(Base() with { ExitPrice = 97.5, ExitReason = ExitReason.Target }).ShouldNotContain("stop_gap", "only a stop exit can be a gap through a stop");
    }

    [Fact]
    public void The_entry_after_a_hole_is_false_for_an_overnight_entry_and_a_trade_can_belong_to_several_categories()
    {
        var overnight = Base() with { DecisionEpochSeconds = Day9(900) - 86400 };

        var categories = Of(overnight);

        categories.ShouldContain("overnight_entry");
        categories.ShouldNotContain("entry_after_hole");
        Of(Base() with { Side = Side.Sell, ExitPrice = 104, EntryFill = EntryFill.AtLimit, TickFallback = true })
            .ShouldBe(["sell", "stop_gap", "entry_at_limit", "tick_fallback"], "in the order of the table");
    }

    [Fact]
    public void The_first_evaluation_is_the_first_entry_time_of_the_recorded_definition_and_false_one_bar_later_or_when_the_text_does_not_parse()
    {
        var at = (int minute) => Base() with { DecisionEpochSeconds = Day9(minute) };

        Of(at(570), Orb15).ShouldContain("first_evaluation");
        Of(at(585), Orb15).ShouldNotContain("first_evaluation", "one bar later");
        Of(at(585), Orb30).ShouldContain("first_evaluation");
        Of(at(570), Orb30).ShouldNotContain("first_evaluation");
        Of(at(570), "{not json").ShouldNotContain("first_evaluation");
        Of(at(570), "{\"id\":\"ORB\"}").ShouldNotContain("first_evaluation");
        Of(at(570), "{\"time_rules\":{\"first_entry\":\"9.30\"}}").ShouldNotContain("first_evaluation");
        EdgeCategories.FirstEntryMinute("{\"time_rules\":{\"first_entry\":\"09:45\"}}").ShouldBe(585);
        EdgeCategories.FirstEntryMinute(null).ShouldBeNull();
    }

    [Fact]
    public void The_table_has_thirteen_distinct_codes_and_every_exit_reason_and_entry_fill_has_a_category_or_a_recorded_reason_for_having_none()
    {
        EdgeCategories.Codes.Count.ShouldBe(13);
        EdgeCategories.Codes.Distinct().Count().ShouldBe(13);
        foreach (var reason in Enum.GetValues<ExitReason>())
        {
            EdgeCategories.ExitReasonHandling.ShouldContainKey(reason, "a new exit reason needs a category or a reason for having none");
        }

        foreach (var fill in Enum.GetValues<EntryFill>())
        {
            EdgeCategories.EntryFillHandling.ShouldContainKey(fill);
        }

        foreach (var handling in EdgeCategories.ExitReasonHandling.Values.Concat(EdgeCategories.EntryFillHandling.Values).Where(h => !h.StartsWith("none", StringComparison.Ordinal)))
        {
            handling.Split(',', StringSplitOptions.TrimEntries).ShouldAllBe(code => EdgeCategories.Codes.Contains(code));
        }
    }
}
