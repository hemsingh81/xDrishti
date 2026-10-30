using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Tests.Recon;

/// <summary>P12-06 criterion 3: duplicated, delayed and reordered updates give the same final leg state as the in-order stream; a late lower status is counted and ignored; the filled quantity never decreases.</summary>
public sealed class UpdateApplierPropertyTests
{
    private const string Cid = ReconFixtures.Cid;

    // A legal sequence: transit, pending, some part fills with a rising cumulative quantity, then one final update.
    private static List<OrderUpdate> Legal(Random rng, int qty)
    {
        var list = new List<OrderUpdate>();
        var t = 0;
        OrderUpdate Make(UpdateStatus s, int traded) => new("9001", Cid, s, 1, ExecSide.Buy, "INTRADAY", "LIMIT", qty, traded, 100m, 0m, traded > 0 ? 100m + traded : 0m, null, null, "P", $"t{++t:D3}");
        list.Add(Make(UpdateStatus.Transit, 0));
        list.Add(Make(UpdateStatus.Pending, 0));
        var cumulative = 0;
        for (var i = 0; i < rng.Next(0, 4) && cumulative < qty - 1; i++)
        {
            cumulative += rng.Next(1, Math.Max(2, qty - cumulative));
            cumulative = Math.Min(cumulative, qty - 1);
            list.Add(Make(UpdateStatus.Pending, cumulative));
        }

        list.Add(rng.Next(3) switch
        {
            0 => Make(UpdateStatus.Traded, qty),
            1 => Make(UpdateStatus.Cancelled, cumulative),
            _ => Make(UpdateStatus.Expired, cumulative),
        });
        return list;
    }

    private static (LegStatus Status, int Filled, int Stale) Final(IEnumerable<OrderUpdate> updates, int qty, bool naive = false)
    {
        var legs = new LegBook();
        var tracked = legs.Add(Cid, 361, LegKind.Entry, qty);
        var counters = new UpdateCounters();
        var applier = new UpdateApplier(legs, counters, new FakeTimeProvider());
        if (naive)
        {
            // The control: last write wins, no rank, no dedup, a quantity that may go down.
            LegStatus status = LegStatus.New;
            var filled = 0;
            foreach (var u in updates)
            {
                (status, filled) = (u.LegStatus, u.TradedQty);
            }

            return (status, filled, 0);
        }

        foreach (var u in updates)
        {
            applier.Apply(u);
        }

        return (tracked.Leg.Status, tracked.Leg.FilledQty, tracked.Leg.StaleUpdates);
    }

    private static List<OrderUpdate> Disturbed(Random rng, List<OrderUpdate> inOrder)
    {
        var copies = inOrder.SelectMany(u => Enumerable.Repeat(u, rng.Next(1, 4))).ToList();
        return [.. copies.OrderBy(_ => rng.Next())];
    }

    [Fact]
    public void Over_2000_seeds_duplicated_delayed_and_reordered_updates_reach_the_state_of_the_in_order_stream()
    {
        for (var seed = 0; seed < 2000; seed++)
        {
            var rng = new Random(seed);
            var qty = rng.Next(5, 21);
            var inOrder = Legal(rng, qty);
            var expected = Final(inOrder, qty);

            var actual = Final(Disturbed(rng, inOrder), qty);

            (actual.Status, actual.Filled).ShouldBe((expected.Status, expected.Filled), $"seed {seed}");
        }
    }

    [Fact]
    public void Negative_control_an_applier_without_rank_is_caught_on_at_least_10_of_the_seeds()
    {
        var caught = 0;
        for (var seed = 0; seed < 2000; seed++)
        {
            var rng = new Random(seed);
            var qty = rng.Next(5, 21);
            var inOrder = Legal(rng, qty);
            var expected = Final(inOrder, qty);
            var naive = Final(Disturbed(rng, inOrder), qty, naive: true);
            if ((naive.Status, naive.Filled) != (expected.Status, expected.Filled))
            {
                caught++;
            }
        }

        caught.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void The_filled_quantity_never_decreases_along_any_arrival_order()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var rng = new Random(seed);
            var qty = rng.Next(5, 21);
            var legs = new LegBook();
            var tracked = legs.Add(Cid, 361, LegKind.Entry, qty);
            var applier = new UpdateApplier(legs, new UpdateCounters(), new FakeTimeProvider());
            var last = 0;
            foreach (var u in Disturbed(rng, Legal(rng, qty)))
            {
                applier.Apply(u);
                tracked.Leg.FilledQty.ShouldBeGreaterThanOrEqualTo(last, $"seed {seed}");
                last = tracked.Leg.FilledQty;
            }
        }
    }

    [Fact]
    public void A_repeat_is_a_duplicate_a_late_lower_status_is_stale_and_both_are_counted_not_applied()
    {
        var legs = new LegBook();
        legs.Add(Cid, 361, LegKind.Entry, 10);
        var counters = new UpdateCounters();
        var applier = new UpdateApplier(legs, counters, new FakeTimeProvider());
        var traded = OrderUpdateParserFor("t2", "TRADED", 10);

        applier.Apply(traded).ShouldBe(ApplyOutcome.Applied);
        applier.Apply(traded).ShouldBe(ApplyOutcome.Duplicate);
        applier.Apply(OrderUpdateParserFor("t1", "PENDING", 0)).ShouldBe(ApplyOutcome.Stale);

        (counters.Applied, counters.Ignored).ShouldBe((1, 2));
        legs.ByClient(Cid)!.Leg.StaleUpdates.ShouldBe(1);
    }

    [Fact]
    public void An_update_with_our_prefix_and_no_leg_is_reported_for_the_reconciliation_and_one_without_the_prefix_is_foreign()
    {
        var applier = new UpdateApplier(new LegBook(), new UpdateCounters(), new FakeTimeProvider());

        applier.Apply(OrderUpdateParserFor("t1", "PENDING", 0)).ShouldBe(ApplyOutcome.UnknownXd);
        applier.UnknownXd.ShouldBe(["9001"]);
        applier.Apply(OrderUpdateParserFor("t2", "PENDING", 0, cid: "MANUAL-1")).ShouldBe(ApplyOutcome.Foreign);
        applier.Apply(OrderUpdateParserFor("t3", "PENDING", 0, cid: null)).ShouldBe(ApplyOutcome.Foreign);
    }

    [Fact]
    public void A_leg_the_broker_created_under_our_super_order_is_found_by_its_broker_order_id()
    {
        var legs = new LegBook();
        legs.Add("XD261030000169S2", 361, LegKind.Stop, 10, brokerOrderId: "S77");
        var applier = new UpdateApplier(legs, new UpdateCounters(), new FakeTimeProvider());

        var outcome = applier.Apply(new OrderUpdate("S77", null, UpdateStatus.Pending, 2, ExecSide.Sell, "INTRADAY", "STOP_LOSS", 10, 0, 98m, 99m, 0m, null, null, "P", "t1"));

        outcome.ShouldBe(ApplyOutcome.Applied);
        legs.ByBroker("S77")!.Leg.Status.ShouldBe(LegStatus.Pending);
    }

    [Fact]
    public void The_average_price_of_the_provisional_fills_moves_only_when_the_filled_quantity_grows()
    {
        var legs = new LegBook();
        var tracked = legs.Add(Cid, 361, LegKind.Entry, 10);
        var applier = new UpdateApplier(legs, new UpdateCounters(), new FakeTimeProvider());

        applier.Apply(new OrderUpdate("9001", Cid, UpdateStatus.Pending, 1, ExecSide.Buy, "INTRADAY", "LIMIT", 10, 4, 100m, 0m, 100.25m, null, null, "P", "t1"));
        applier.Apply(new OrderUpdate("9001", Cid, UpdateStatus.Pending, 1, ExecSide.Buy, "INTRADAY", "LIMIT", 10, 4, 100m, 0m, 77m, null, null, "P", "t2"));

        tracked.AvgPrice.ShouldBe(100.25m);
    }

    private static OrderUpdate OrderUpdateParserFor(string time, string status, int traded, string? cid = Cid) =>
        OrderUpdateParser.Parse(ReconFixtures.Msg("9001", cid, status, 10, traded, time)).Update!;
}
