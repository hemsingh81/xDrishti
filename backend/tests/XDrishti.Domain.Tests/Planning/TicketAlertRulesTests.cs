using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>Spec P7-20 (ADR 0115): the pure rules of price crossings, the owner's acts and the post-trade prompt.</summary>
public sealed class TicketAlertRulesTests
{
    private static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    // Monday 2026-10-26 is a session; its window is Tue-Thu.
    private static readonly DateOnly Monday = new(2026, 10, 26);

    private static readonly TradingCalendar Calendar = new();

    private static readonly decimal[] Prices = [99m, 95m, 94.5m, 110.2m];

    [Fact]
    public void A_buy_fires_the_stop_once_at_or_below_and_the_target_at_or_above_and_nothing_between()
    {
        var kinds = Prices.Select(p => string.Join(',', TicketCrossing.Crossed("BUY", 95m, 110m, p))).ToList();

        kinds.ShouldBe(["", CrossingKinds.Stop, CrossingKinds.Stop, CrossingKinds.Target]);
    }

    [Fact]
    public void A_sell_is_the_mirror_and_fires_just_past_each_level()
    {
        TicketCrossing.Crossed("SELL", 210m, 180m, 209.95m).ShouldBeEmpty();
        TicketCrossing.Crossed("SELL", 210m, 180m, 210.05m).ShouldBe([CrossingKinds.Stop]);
        TicketCrossing.Crossed("SELL", 210m, 180m, 179.95m).ShouldBe([CrossingKinds.Target]);
    }

    [Fact]
    public void A_null_level_cannot_fire_and_an_unknown_side_fires_nothing()
    {
        TicketCrossing.Crossed("BUY", null, null, 1m).ShouldBeEmpty();
        TicketCrossing.Crossed("BUY", null, 110m, 120m).ShouldBe([CrossingKinds.Target]);
        TicketCrossing.Crossed("HOLD", 95m, 110m, 1m).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(3, 44, 59, false)] // 09:14:59 India
    [InlineData(3, 45, 0, true)] // 09:15:00
    [InlineData(9, 59, 59, true)] // 15:29:59
    [InlineData(10, 0, 0, false)] // 15:30:00
    public void Only_ticks_from_0915_up_to_1530_are_in_the_session(int hour, int minute, int second, bool expected)
    {
        TicketCrossing.InSession(new DateTimeOffset(2026, 10, 26, hour, minute, second, TimeSpan.Zero)).ShouldBe(expected);
    }

    [Fact]
    public void A_crossing_counts_until_the_end_of_the_next_india_day_across_midnight_and_a_weekend()
    {
        var friday = new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

        TicketCrossing.IsLive(friday, new DateTimeOffset(2026, 10, 24, 18, 0, 0, TimeSpan.Zero)).ShouldBeTrue(); // 23:30 Saturday India
        TicketCrossing.IsLive(friday, new DateTimeOffset(2026, 10, 24, 18, 30, 0, TimeSpan.Zero)).ShouldBeFalse(); // 00:00 Sunday India
    }

    [Fact]
    public void The_texts_name_the_anchor_never_say_a_stop_was_hit_and_always_end_with_the_no_order_words()
    {
        var level = TicketCrossing.Text(CrossingKinds.Stop, "level", Monday, 95m, 94.5m);
        var close = TicketCrossing.Text(CrossingKinds.Stop, "close_of_d", Monday, 95m, 94.5m);
        var target = TicketCrossing.Text(CrossingKinds.Target, null, Monday, 110m, 110.2m);

        level.ShouldContain("stop level 95.00 crossed");
        close.ShouldContain("indicative stop from the close of 2026-10-26 crossed");
        target.ShouldContain("target level 110.00 crossed");
        foreach (var text in new[] { level, close, target })
        {
            text.ShouldEndWith("Alert only: no order is placed and none exists. A stop is only an alert.");
            text.ShouldNotContain("hit");
        }
    }

    [Fact]
    public void The_newest_act_decides_a_snooze_ends_by_itself_and_snoozes_are_capped_at_three()
    {
        var now = new DateTimeOffset(2026, 10, 26, 6, 0, 0, TimeSpan.Zero);
        var snoozed = new[] { new AlertAck(1, AlertAcks.Snooze, now.AddMinutes(30)) };

        AlertAcks.Visibility([], now).ShouldBe(AlertVisibility.Shown);
        AlertAcks.Visibility(snoozed, now).ShouldBe(AlertVisibility.Snoozed);
        AlertAcks.Visibility(snoozed, now.AddMinutes(31)).ShouldBe(AlertVisibility.Shown);
        AlertAcks.Visibility([.. snoozed, new AlertAck(2, AlertAcks.Dismiss, null)], now).ShouldBe(AlertVisibility.Dismissed);
        AlertAcks.CanSnooze([new(1, AlertAcks.Snooze, now), new(2, AlertAcks.Snooze, now), new(3, AlertAcks.Snooze, now)], now.AddHours(1)).ShouldBeFalse();
        AlertAcks.CanSnooze([new(1, AlertAcks.Snooze, now), new(2, AlertAcks.Snooze, now)], now.AddHours(1)).ShouldBeTrue();
    }

    private static PromptFacts Prompt(long id, long item, DateOnly? session = null) => new(id, item, Account, "ALRT", Side.Buy, session ?? Monday);

    private static PromptTrade Trade(long? journalItem = null, Side side = Side.Buy) => new(Account, "ALRT", side, Monday, journalItem);

    [Fact]
    public void With_no_trade_the_prompt_is_open_through_the_third_trading_day_and_then_expires()
    {
        PromptRules.Evaluate([Prompt(1, 10)], [], new DateOnly(2026, 10, 29), Calendar, 3).Single().Status.ShouldBe(PromptStatus.Open);
        PromptRules.Evaluate([Prompt(1, 10)], [], new DateOnly(2026, 10, 30), Calendar, 3).Single().Status.ShouldBe(PromptStatus.ExpiredNoFill);
    }

    [Fact]
    public void A_wednesday_holiday_moves_the_window_to_friday()
    {
        var holiday = new TradingCalendar([TradingDay.Holiday(new DateOnly(2026, 10, 28), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);

        PromptRules.Evaluate([Prompt(1, 10)], [], new DateOnly(2026, 10, 30), holiday, 3).Single().Status.ShouldBe(PromptStatus.Open);
        PromptRules.Evaluate([Prompt(1, 10)], [], new DateOnly(2026, 11, 2), holiday, 3).Single().Status.ShouldBe(PromptStatus.ExpiredNoFill);
    }

    [Fact]
    public void A_trade_of_the_same_key_matches_via_the_journal_when_linked_and_as_a_trade_on_record_when_not()
    {
        var linked = PromptRules.Evaluate([Prompt(1, 10)], [Trade(10)], Monday, Calendar, 3).Single();
        var unlinked = PromptRules.Evaluate([Prompt(1, 10)], [Trade()], Monday, Calendar, 3).Single();
        var otherSide = PromptRules.Evaluate([Prompt(1, 10)], [Trade(side: Side.Sell)], Monday, Calendar, 3).Single();

        (linked.Status, linked.Via).ShouldBe((PromptStatus.Matched, PromptVia.Journal));
        (unlinked.Status, unlinked.Via).ShouldBe((PromptStatus.Matched, PromptVia.TradeOnRecord));
        otherSide.Status.ShouldBe(PromptStatus.Open);
        PromptRules.Text(unlinked).ShouldContain("broker-synced fills only");
    }

    [Fact]
    public void Two_tickets_that_fit_one_trade_are_ambiguous_with_their_own_text_and_name_each_other()
    {
        var results = PromptRules.Evaluate([Prompt(1, 10), Prompt(2, 11)], [Trade()], Monday, Calendar, 3);

        results.ShouldAllBe(r => r.Status == PromptStatus.Ambiguous);
        results[0].SameTradeItems.ShouldBe([11L]);
        results[1].SameTradeItems.ShouldBe([10L]);
        PromptRules.BellText(PromptStatus.Ambiguous).ShouldNotBe(PromptRules.BellText(PromptStatus.Open));
        PromptRules.Text(results[0]).ShouldContain("item 11");
    }

    [Fact]
    public void A_trade_the_journal_gave_to_another_ticket_does_not_make_this_prompt_ambiguous()
    {
        var results = PromptRules.Evaluate([Prompt(1, 10), Prompt(2, 11)], [Trade(10)], Monday, Calendar, 3);

        results[0].Status.ShouldBe(PromptStatus.Matched);
        results[1].Status.ShouldBe(PromptStatus.Open);
    }
}
