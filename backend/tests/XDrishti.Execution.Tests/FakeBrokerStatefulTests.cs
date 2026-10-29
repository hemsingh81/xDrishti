using Microsoft.Extensions.Time.Testing;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Tests;

/// <summary>P12-02 criteria 12 and 13 (P12-02b): the stateful fake, and the contract every <see cref="IBrokerOrders"/> / <see cref="IBrokerReads"/> implementation must meet (run here against the fake only; the sandbox is never called).</summary>
public sealed class FakeBrokerStatefulTests
{
    private static BrokerLeg Leg(int seq, ExecSide side = ExecSide.Buy, int qty = 10, decimal price = 100m, LegKind kind = LegKind.Entry) =>
        new(ClientOrderId.Create(new DateOnly(2026, 10, 26), seq, kind, 1).Value.ToString(), kind, side, "1594", qty, price);

    [Fact]
    public async Task A_buy_limit_fills_when_the_path_reaches_it_and_a_sell_limit_when_it_rises_to_it()
    {
        var broker = new FakeBroker();
        var buy = Leg(1, ExecSide.Buy, price: 100m);
        var sell = Leg(2, ExecSide.Sell, price: 105m);
        await broker.PlaceAsync(buy, CancellationToken.None);
        await broker.PlaceAsync(sell, CancellationToken.None);
        broker.StatusOf(buy.ClientOrderId).ShouldBe("PENDING");

        broker.SetPrice("1594", 102m);
        broker.StatusOf(buy.ClientOrderId).ShouldBe("PENDING");
        broker.SetPrice("1594", 100m);
        broker.StatusOf(buy.ClientOrderId).ShouldBe("TRADED");
        broker.StatusOf(sell.ClientOrderId).ShouldBe("PENDING");
        broker.SetPrice("1594", 106m);
        broker.StatusOf(sell.ClientOrderId).ShouldBe("TRADED");
        (await broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(0);
    }

    [Fact]
    public async Task Partial_fills_accumulate_and_a_final_order_is_never_changed()
    {
        var broker = new FakeBroker { MaxFillPerStep = 4 };
        var leg = Leg(1, qty: 10);
        await broker.PlaceAsync(leg, CancellationToken.None);

        broker.SetPrice("1594", 99m);
        (broker.StatusOf(leg.ClientOrderId), broker.FilledOf(leg.ClientOrderId)).ShouldBe(("PART_TRADED", 4));
        broker.SetPrice("1594", 99m);
        broker.SetPrice("1594", 99m);
        (broker.StatusOf(leg.ClientOrderId), broker.FilledOf(leg.ClientOrderId)).ShouldBe(("TRADED", 10));
        broker.SetPrice("1594", 99m);
        broker.FilledOf(leg.ClientOrderId).ShouldBe(10);
        (await broker.ModifyAsync(leg.ClientOrderId, 101m, CancellationToken.None)).Reason.ShouldBe("order_not_open");
        (await broker.CancelAsync(leg.ClientOrderId, CancellationToken.None)).Reason.ShouldBe("order_not_open");
        (await broker.GetTradeBookAsync(CancellationToken.None)).Value!.Sum(t => t.Qty).ShouldBe(10);
        (await broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(10);
    }

    [Fact]
    public async Task The_26th_modification_is_refused_and_a_cancelled_order_stays_in_the_book_as_cancelled()
    {
        var clock = new FakeTimeProvider();
        var broker = new FakeBroker(clock);
        var leg = Leg(1);
        await broker.PlaceAsync(leg, CancellationToken.None);

        for (var i = 1; i <= FakeBroker.ModificationCap; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            (await broker.ModifyAsync(leg.ClientOrderId, 100m + (i * 0.05m), CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        (await broker.ModifyAsync(leg.ClientOrderId, 200m, CancellationToken.None)).Reason.ShouldBe("modification_cap");
        (await broker.CancelAsync(leg.ClientOrderId, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        (await broker.GetOrderBookAsync(CancellationToken.None)).Value!.Single().Status.ShouldBe("CANCELLED");
        (await broker.LookupAsync(leg.ClientOrderId, CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_expired_token_refuses_writes_and_makes_reads_unreadable_and_end_of_day_expires_what_is_open()
    {
        var broker = new FakeBroker();
        var open = Leg(1);
        await broker.PlaceAsync(open, CancellationToken.None);
        broker.EndOfDay();
        broker.StatusOf(open.ClientOrderId).ShouldBe("EXPIRED");

        broker.TokenExpired = true;
        (await broker.PlaceAsync(Leg(2), CancellationToken.None)).Reason.ShouldBe("token_invalid");
        broker.Legs.Count.ShouldBe(1);
        (await broker.GetFundsAsync(CancellationToken.None)).Readable.ShouldBeFalse();
        (await broker.GetOrderBookAsync(CancellationToken.None)).Readable.ShouldBeFalse();
    }

    [Fact]
    public async Task More_than_ten_writes_in_a_second_answer_the_broker_rate_limit_and_the_window_slides()
    {
        var clock = new FakeTimeProvider();
        var broker = new FakeBroker(clock);
        for (var i = 1; i <= 10; i++)
        {
            (await broker.PlaceAsync(Leg(i), CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        }

        (await broker.PlaceAsync(Leg(11), CancellationToken.None)).Reason.ShouldBe("rate_limited_by_broker");
        broker.Legs.Count.ShouldBe(10);
        clock.Advance(TimeSpan.FromSeconds(1));
        (await broker.PlaceAsync(Leg(11), CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
    }

    [Fact]
    public async Task A_coded_rejection_touches_nothing_and_clears()
    {
        var broker = new FakeBroker { RejectNextWith = "broker_rejected" };

        (await broker.PlaceAsync(Leg(1), CancellationToken.None)).Reason.ShouldBe("broker_rejected");
        broker.Legs.ShouldBeEmpty();
        (await broker.PlaceAsync(Leg(1), CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
    }

    // Property (500 seeds): over random scripted broker behaviour a client id is never accepted twice, and the fills never exceed the quantity.
    [Fact]
    public async Task Over_random_scripts_no_client_id_is_accepted_twice_and_fills_never_exceed_the_quantity()
    {
        for (var seed = 1; seed <= 500; seed++)
        {
            var rnd = new Random(seed);
            var broker = new FakeBroker(new FakeTimeProvider()) { MaxFillPerStep = rnd.Next(0, 6) };
            var legs = Enumerable.Range(1, 5).Select(i => Leg(i, rnd.Next(2) == 0 ? ExecSide.Buy : ExecSide.Sell, rnd.Next(1, 20), 100m + rnd.Next(-3, 4))).ToList();
            var accepted = new Dictionary<string, HashSet<string>>();
            for (var step = 0; step < 40; step++)
            {
                var leg = legs[rnd.Next(legs.Count)];
                switch (rnd.Next(5))
                {
                    case 0:
                        broker.DropNextResponse = rnd.Next(3) == 0;
                        var ack = await broker.PlaceAsync(leg, CancellationToken.None);
                        if (ack.Outcome == BrokerOutcome.Acknowledged)
                        {
                            accepted.TryAdd(leg.ClientOrderId, []);
                            accepted[leg.ClientOrderId].Add(ack.BrokerOrderId!);
                        }

                        break;
                    case 1:
                        await broker.ModifyAsync(leg.ClientOrderId, 100m + rnd.Next(-3, 4), CancellationToken.None);
                        break;
                    case 2:
                        await broker.CancelAsync(leg.ClientOrderId, CancellationToken.None);
                        break;
                    default:
                        broker.SetPrice("1594", 100m + rnd.Next(-4, 5));
                        break;
                }
            }

            accepted.Values.ShouldAllBe(ids => ids.Count == 1, $"seed {seed}");
            foreach (var l in legs)
            {
                broker.FilledOf(l.ClientOrderId).ShouldBeLessThanOrEqualTo(l.Qty, $"seed {seed}");
            }

            broker.Legs.Count.ShouldBe(broker.Legs.Select(l => l.ClientOrderId).Distinct().Count(), $"seed {seed}");
        }
    }
}

/// <summary>The contract of the broker ports. It is abstract so that any implementation (the fake here, a recorded-answer double of the Dhan client in its own tests) is held to the same table. The explicit sandbox smoke is NOT part of this slice (it would call Dhan).</summary>
public abstract class BrokerContractTests
{
    protected abstract (IBrokerOrders Orders, IBrokerReads Reads) Make();

    private static readonly BrokerLeg Leg = new(ClientOrderId.Create(new DateOnly(2026, 10, 26), 361, LegKind.Entry, 1).Value.ToString(), LegKind.Entry, ExecSide.Buy, "1594", 10, 100m);

    [Fact]
    public async Task Place_then_lookup_by_client_id_finds_it_and_an_unknown_id_is_absent_not_an_error()
    {
        var (orders, _) = Make();

        (await orders.PlaceAsync(Leg, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        (await orders.LookupAsync(Leg.ClientOrderId, CancellationToken.None)).ShouldNotBeNull().Qty.ShouldBe(10);
        (await orders.LookupAsync("XD0000000000ZZZZ", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Modify_and_cancel_of_an_open_order_are_acknowledged_and_of_an_absent_one_are_not_sent()
    {
        var (orders, _) = Make();
        await orders.PlaceAsync(Leg, CancellationToken.None);

        (await orders.ModifyAsync(Leg.ClientOrderId, 101m, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        (await orders.CancelAsync(Leg.ClientOrderId, CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.Acknowledged);
        (await orders.CancelAsync("XD0000000000ZZZZ", CancellationToken.None)).Outcome.ShouldBe(BrokerOutcome.NotSent);
    }

    [Fact]
    public async Task The_four_reads_return_typed_views_that_agree_with_what_was_placed()
    {
        var (orders, reads) = Make();
        await orders.PlaceAsync(Leg, CancellationToken.None);

        var book = await reads.GetOrderBookAsync(CancellationToken.None);
        book.Readable.ShouldBeTrue();
        book.Value!.Single().ClientOrderId.ShouldBe(Leg.ClientOrderId);
        (await reads.GetTradeBookAsync(CancellationToken.None)).Readable.ShouldBeTrue();
        (await reads.GetPositionsAsync(CancellationToken.None)).Readable.ShouldBeTrue();
        (await reads.GetFundsAsync(CancellationToken.None)).Value!.Available.ShouldBeGreaterThan(0m);
    }
}

public sealed class FakeBrokerContractTests : BrokerContractTests
{
    protected override (IBrokerOrders Orders, IBrokerReads Reads) Make()
    {
        var broker = new FakeBroker(new FakeTimeProvider());
        return (broker, broker);
    }
}
