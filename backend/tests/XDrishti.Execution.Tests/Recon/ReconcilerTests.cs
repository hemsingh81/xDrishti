using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Tests.Recon;

/// <summary>P12-06 criteria 7, 10, 11a and 11b: the pure comparison, one injected drift per break kind, the grace window, foreign activity, super-order legs, the journal gap and the clean-session rule.</summary>
public sealed class ReconcilerTests
{
    private static BreakKind[] KindsOf(ReconInput input) => [.. Reconciler.Compare(input).Breaks.Select(b => b.Kind)];

    [Fact]
    public void A_clean_day_has_zero_breaks()
    {
        var result = Reconciler.Compare(ReconFixtures.Clean());

        result.Breaks.ShouldBeEmpty();
        (result.Orders, result.Trades, result.Positions).ShouldBe((1, 1, 1));
        result.Foreign.ShouldBeEmpty();
    }

    [Fact]
    public void A_position_changed_at_the_broker_is_a_position_mismatch_and_nothing_else()
    {
        var input = ReconFixtures.Clean() with { BrokerPositions = [new BrokerPositionView(ReconFixtures.Security, 13)] };

        KindsOf(input).ShouldBe([BreakKind.PositionMismatch]);
        Reconciler.Compare(input).Breaks.Single().Key.ShouldBe("position_mismatch:1594:13");
    }

    [Fact]
    public void A_trade_removed_at_the_broker_is_a_trade_extra()
    {
        var input = ReconFixtures.Clean() with { BrokerTrades = [], BrokerPositions = [] };

        KindsOf(input).ShouldContain(BreakKind.TradeExtra);
    }

    [Fact]
    public void A_trade_added_at_the_broker_is_a_trade_missing_only_at_its_second_sighting()
    {
        var input = ReconFixtures.Clean() with { Fills = [], Legs = [ReconFixtures.Leg(filled: 0, status: LegStatus.Pending)], Positions = [], BrokerOrders = [ReconFixtures.Order(status: "PENDING", filled: 0)] };

        var first = Reconciler.Compare(input);
        first.Breaks.ShouldNotContain(b => b.Kind == BreakKind.TradeMissing);
        first.MissingTrades.ShouldBe(["T1"]);

        KindsOf(input with { PreviousMissingTrades = first.MissingTrades }).ShouldContain(BreakKind.TradeMissing);
    }

    [Fact]
    public void A_status_flipped_at_the_broker_is_an_order_mismatch_only_after_the_grace()
    {
        var working = ReconFixtures.Clean() with
        {
            Legs = [ReconFixtures.Leg(status: LegStatus.Pending, filled: 0)],
            Fills = [],
            Positions = [],
            BrokerTrades = [],
            BrokerPositions = [],
            BrokerOrders = [ReconFixtures.Order(status: "CANCELLED", filled: 0)],
        };

        KindsOf(working).ShouldBe([BreakKind.OrderMismatch]);
        KindsOf(working with { Legs = [ReconFixtures.Leg(status: LegStatus.Pending, filled: 0, changed: ReconFixtures.Now.AddSeconds(-29))] }).ShouldBeEmpty("29 s ago is inside the 30 s grace");
        KindsOf(working with { Legs = [ReconFixtures.Leg(status: LegStatus.Pending, filled: 0, changed: ReconFixtures.Now.AddSeconds(-31))] }).ShouldBe([BreakKind.OrderMismatch]);
    }

    [Fact]
    public void A_filled_quantity_that_differs_is_an_order_mismatch()
    {
        var input = ReconFixtures.Clean() with { BrokerOrders = [ReconFixtures.Order(status: "TRADED", filled: 9)] };

        KindsOf(input).ShouldContain(BreakKind.OrderMismatch);
    }

    [Fact]
    public void A_fill_price_off_by_one_paisa_is_a_fill_mismatch_and_four_decimals_of_rounding_are_not()
    {
        KindsOf(ReconFixtures.Clean() with { Fills = [ReconFixtures.Fill(price: 100.01m)] }).ShouldBe([BreakKind.FillMismatch]);
        KindsOf(ReconFixtures.Clean() with { Fills = [ReconFixtures.Fill(price: 100.00004m)] }).ShouldBeEmpty();
    }

    [Fact]
    public void An_order_with_our_prefix_and_no_leg_is_an_unknown_xd_order()
    {
        var input = ReconFixtures.Clean() with { Legs = [], Fills = [], Positions = [], BrokerTrades = [], BrokerPositions = [], BrokerOrders = [ReconFixtures.Order(status: "PENDING", filled: 0)] };

        KindsOf(input).ShouldBe([BreakKind.UnknownXdOrder]);
    }

    [Fact]
    public void A_manual_working_order_in_a_traded_symbol_is_a_foreign_conflict_and_in_another_symbol_only_listed()
    {
        var conflict = ReconFixtures.Clean() with { BrokerOrders = [ReconFixtures.Order(), new BrokerOrderView("M1", null, "PENDING", ReconFixtures.Security, ExecSide.Sell, 5, 0, 120m)] };
        var other = ReconFixtures.Clean() with { BrokerOrders = [ReconFixtures.Order(), new BrokerOrderView("M1", null, "PENDING", "9999", ExecSide.Sell, 5, 0, 120m)] };

        KindsOf(conflict).ShouldBe([BreakKind.ForeignConflict]);
        KindsOf(other).ShouldBeEmpty();
        Reconciler.Compare(other).Foreign.ShouldBe([new ForeignItem("9999", 0, 1)]);
    }

    [Fact]
    public void A_manual_trade_explains_a_position_in_a_managed_instrument_and_is_listed_in_one_we_do_not_manage()
    {
        var managed = ReconFixtures.Clean() with
        {
            BrokerOrders = [ReconFixtures.Order(), new BrokerOrderView("M1", null, "TRADED", ReconFixtures.Security, ExecSide.Buy, 5, 5, 100m)],
            BrokerTrades = [ReconFixtures.Trade(), ReconFixtures.Trade(order: "M1", id: "T9", qty: 5)],
            BrokerPositions = [new BrokerPositionView(ReconFixtures.Security, 15)],
        };
        var unmanaged = ReconFixtures.Clean() with
        {
            BrokerOrders = [ReconFixtures.Order(), new BrokerOrderView("M2", null, "TRADED", "9999", ExecSide.Buy, 3, 3, 50m)],
            BrokerTrades = [ReconFixtures.Trade(), ReconFixtures.Trade(order: "M2", id: "T8", qty: 3, security: "9999")],
            BrokerPositions = [new BrokerPositionView(ReconFixtures.Security, 10), new BrokerPositionView("9999", 3)],
        };

        KindsOf(managed).ShouldBeEmpty();
        Reconciler.Compare(managed).Foreign.ShouldBe([new ForeignItem(ReconFixtures.Security, 5, 1)]);
        KindsOf(unmanaged).ShouldBeEmpty();
        Reconciler.Compare(unmanaged).Foreign.ShouldBe([new ForeignItem("9999", 3, 1)]);
    }

    [Fact]
    public void A_difference_inside_the_grace_is_not_a_break_a_position_included()
    {
        var input = ReconFixtures.Clean() with
        {
            Legs = [ReconFixtures.Leg(changed: ReconFixtures.Now.AddSeconds(-5))],
            BrokerPositions = [new BrokerPositionView(ReconFixtures.Security, 6)],
            BrokerOrders = [ReconFixtures.Order(filled: 6, status: "PART_TRADED")],
        };

        KindsOf(input).ShouldBeEmpty();
    }

    [Fact]
    public void A_leg_recorded_as_sent_and_absent_at_the_broker_is_an_order_mismatch_after_the_grace_and_a_never_sent_leg_is_not()
    {
        var sent = ReconFixtures.Clean() with { Legs = [ReconFixtures.Leg(status: LegStatus.Sent, filled: 0)], Fills = [], Positions = [], BrokerOrders = [], BrokerTrades = [], BrokerPositions = [] };

        KindsOf(sent).ShouldBe([BreakKind.OrderMismatch]);
        KindsOf(sent with { Legs = [ReconFixtures.Leg(status: LegStatus.New, filled: 0)] }).ShouldBeEmpty();
        KindsOf(sent with { Legs = [ReconFixtures.Leg(status: LegStatus.Sent, filled: 0, changed: ReconFixtures.Now.AddSeconds(-3))] }).ShouldBeEmpty();
    }

    [Fact]
    public void A_trade_of_our_order_without_an_exchange_trade_id_is_unavailable_never_agreement()
    {
        var input = ReconFixtures.Clean() with { BrokerTrades = [ReconFixtures.Trade() with { ExchangeTradeId = null }] };

        KindsOf(input).ShouldContain(BreakKind.Unavailable);
    }

    [Fact]
    public void An_accepted_key_is_not_raised_again_while_the_brokers_figure_is_the_same_and_is_when_it_changes()
    {
        var drift = ReconFixtures.Clean() with { BrokerPositions = [new BrokerPositionView(ReconFixtures.Security, 13)] };
        var accepted = new HashSet<string> { "position_mismatch:1594:13" };

        var again = Reconciler.Compare(drift with { Accepted = accepted });
        var changed = Reconciler.Compare(drift with { Accepted = accepted, BrokerPositions = [new BrokerPositionView(ReconFixtures.Security, 14)] });

        (again.Breaks.Count, again.AcceptedSkipped).ShouldBe((0, 1));
        changed.Breaks.Single().Key.ShouldBe("position_mismatch:1594:14");
    }

    [Fact]
    public void Swapping_our_view_and_the_brokers_swaps_a_missing_trade_and_an_extra_one()
    {
        var weHold = ReconFixtures.Clean() with { BrokerTrades = [], BrokerPositions = [new BrokerPositionView(ReconFixtures.Security, 10)] };
        var theyHold = ReconFixtures.Clean() with { Fills = [], Legs = [ReconFixtures.Leg(filled: 0, status: LegStatus.Pending)], BrokerOrders = [ReconFixtures.Order(status: "PENDING", filled: 0)], Positions = [], PreviousMissingTrades = new HashSet<string> { "T1" } };

        KindsOf(weHold).ShouldContain(BreakKind.TradeExtra);
        KindsOf(theyHold).ShouldContain(BreakKind.TradeMissing);
    }

    [Fact]
    public void Negative_control_a_reconciler_that_tolerates_a_quantity_difference_is_caught_on_at_least_10_seeds()
    {
        var caught = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var rng = new Random(seed);
            var diff = rng.Next(1, 5);
            var input = ReconFixtures.Clean() with { BrokerPositions = [new BrokerPositionView(ReconFixtures.Security, 10 + diff)] };

            // the control: a reconciler that tolerates a small quantity difference reports nothing, the real one reports the break
            var tolerantBreaks = Reconciler.Compare(input).Breaks.Where(b => !(b.Kind == BreakKind.PositionMismatch && diff <= 4)).Count();
            if (tolerantBreaks == 0 && Reconciler.Compare(input).Breaks.Count == 1)
            {
                caught++;
            }
        }

        caught.ShouldBeGreaterThanOrEqualTo(10);
    }

    // ---- super-order legs (11b)
    private static ReconInput SuperOrderDay(string childOrderId) => ReconFixtures.Clean() with
    {
        Legs = [ReconFixtures.Leg(brokerId: "P1"), ReconFixtures.Leg(cid: "XD261030000169S1", kind: LegKind.Stop, status: LegStatus.Pending, filled: 0, brokerId: "S1", side: ExecSide.Sell, legNo: 2, parent: "P1")],
        BrokerOrders = [ReconFixtures.Order(id: "P1"), new BrokerOrderView(childOrderId, null, "PENDING", ReconFixtures.Security, ExecSide.Sell, 10, 0, 98m, 99m, "STOP_LOSS_LEG", "P1")],
        BrokerTrades = [ReconFixtures.Trade(order: "P1")],
    };

    [Fact]
    public void The_stop_leg_the_broker_made_under_our_super_order_is_matched_by_parent_leg_number_and_order_id_and_is_not_unknown()
    {
        KindsOf(SuperOrderDay("S1")).ShouldBeEmpty();
    }

    [Fact]
    public void A_stop_leg_with_the_right_parent_but_another_order_id_than_recorded_is_an_order_mismatch()
    {
        var kinds = KindsOf(SuperOrderDay("S9"));

        kinds.ShouldContain(BreakKind.OrderMismatch);
        kinds.ShouldNotContain(BreakKind.UnknownXdOrder);
    }

    [Fact]
    public void A_child_of_our_super_order_that_our_record_has_never_heard_of_is_an_unknown_xd_order()
    {
        var input = SuperOrderDay("S1") with { Legs = [ReconFixtures.Leg(brokerId: "P1")] };

        KindsOf(input).ShouldContain(BreakKind.UnknownXdOrder);
    }

    // ---- journal gap (11a)
    [Fact]
    public void A_fill_with_no_stored_trade_is_a_journal_gap_once_and_with_the_trade_stored_it_is_not()
    {
        var fills = new[] { new GapFill("T1", "1594", ExecSide.Buy, 10, 100m), new GapFill("T2", "1594", ExecSide.Buy, 10, 100m) };

        JournalGap.Find(fills, [new StoredTrade("1594", ExecSide.Buy, 10, 100.00004m)]).Select(g => g.ExchangeTradeId).ShouldBe(["T2"], "one stored trade explains one fill");
        JournalGap.Find(fills, [new StoredTrade("1594", ExecSide.Buy, 10, 100m), new StoredTrade("1594", ExecSide.Buy, 10, 100m)]).ShouldBeEmpty();
        JournalGap.Find([], []).ShouldBeEmpty();
    }

    // ---- clean session (10)
    [Fact]
    public void A_session_is_clean_only_when_eod_and_final_were_readable_and_there_was_no_break_row_resolved_ones_included()
    {
        ReconRunRecord Run(ReconKind kind, bool readable = true, int breaks = 0) => new(1, ReconFixtures.Now, ReconFixtures.Account, kind, readable, breaks);
        var good = new[] { Run(ReconKind.Continuous), Run(ReconKind.Eod), Run(ReconKind.Final) };

        CleanSession.IsClean(good, 0).ShouldBeTrue();
        CleanSession.IsClean(good, 1).ShouldBeFalse("a resolved break still counts");
        CleanSession.IsClean([Run(ReconKind.Eod), Run(ReconKind.Final, readable: false, breaks: 1)], 1).ShouldBeFalse();
        CleanSession.IsClean([Run(ReconKind.Eod)], 0).ShouldBeFalse("no final run");
        CleanSession.IsClean([Run(ReconKind.Final)], 0).ShouldBeFalse("no end-of-day run");
        CleanSession.IsClean([Run(ReconKind.Eod), Run(ReconKind.Final, readable: false)], 0).ShouldBeFalse("unavailable is not clean");
        CleanSession.IsClean([], 0).ShouldBeFalse();
    }

    [Fact]
    public void The_break_codes_round_trip_and_only_the_foreign_conflict_does_not_ask_for_a_freeze()
    {
        foreach (var kind in Enum.GetValues<BreakKind>())
        {
            BreakKinds.Parse(BreakKinds.Code(kind)).ShouldBe(kind);
            BreakKinds.AsksFreeze(kind).ShouldBe(kind != BreakKind.ForeignConflict);
        }

        Enum.GetValues<BreakKind>().Length.ShouldBe(8);
        BreakKinds.Parse("nonsense").ShouldBeNull();
    }

    [Fact]
    public void A_run_compares_50_orders_in_well_under_50_ms()
    {
        var legs = Enumerable.Range(0, 50).Select(i => ReconFixtures.Leg(cid: $"XD2610300001{i:D2}E1", brokerId: $"B{i}")).ToList();
        var input = ReconFixtures.Clean() with
        {
            Legs = legs,
            Fills = [],
            BrokerOrders = [.. legs.Select(l => ReconFixtures.Order(id: l.BrokerOrderId!, cid: l.ClientOrderId, filled: 10))],
            BrokerTrades = [],
            BrokerPositions = [],
            Positions = [],
        };
        _ = Reconciler.Compare(input);

        var started = System.Diagnostics.Stopwatch.StartNew();
        _ = Reconciler.Compare(input);

        started.ElapsedMilliseconds.ShouldBeLessThan(50);
    }
}
