using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Tests.Protection;

/// <summary>Criteria 8 (the square-off race) and 8a (a late entry fill) of P12-04, on the FakeBroker with seeds.</summary>
public sealed class SquareOffTests
{
    private static void ToIst(Rig rig, int hour, int minute, int second = 0) =>
        rig.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 26, hour, minute, second, TimeSpan.Zero) - TimeSpan.FromMinutes(330));

    [Fact]
    public async Task At_the_square_off_time_the_exit_is_a_reduce_only_marketable_limit_and_the_stop_is_cancelled_only_after_the_position_is_flat()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        ToIst(rig, 15, 15);
        rig.Source.Last = 1500m;

        await rig.SquareOff.TickAsync(CancellationToken.None);
        var exit = rig.Broker.Legs.Single(l => l.Kind == LegKind.Exit);
        (exit.Side, exit.Qty, exit.Price).ShouldBe((ExecSide.Sell, 10, 1492.50m));
        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("PENDING", "the stop stays until the exit fills");

        await rig.SquareOff.TickAsync(CancellationToken.None);
        rig.OrdersOf(LegKind.Exit).ShouldBe(1, "no second exit on top of the first");
        rig.Broker.SetPrice("1594", 1500m);
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        await rig.SquareOff.TickAsync(CancellationToken.None);

        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("CANCELLED");
        (await rig.Broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(0);
    }

    [Fact]
    public async Task Nothing_is_exited_before_the_square_off_time_and_nothing_at_all_for_a_closed_gate()
    {
        var early = new Rig();
        await early.FilledAsync();
        await early.StopAsync();
        ToIst(early, 15, 14, 59);
        await early.SquareOff.TickAsync(CancellationToken.None);
        early.OrdersOf(LegKind.Exit).ShouldBe(0);

        var closed = new Rig(mode: new ModeAnswer(ExecMode.SemiAuto, false));
        await closed.FilledAsync();
        await closed.StopAsync();
        ToIst(closed, 15, 15);
        await closed.SquareOff.TickAsync(CancellationToken.None);
        closed.OrdersOf(LegKind.Exit).ShouldBe(0);
    }

    [Fact]
    public async Task A_super_order_target_leg_that_cannot_be_cancelled_here_stops_the_exit_with_exit_failed()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        ToIst(rig, 15, 15);
        rig.Source.Last = 1500m;
        rig.Source.BookOverride = [.. (await rig.Broker.GetOrderBookAsync(CancellationToken.None)).Value!, new BrokerOrderView("B77", null, "PENDING", "1594", ExecSide.Sell, 10, 0, 1530m, null, "TARGET_LEG")];

        await rig.SquareOff.TickAsync(CancellationToken.None);

        rig.IsOpen(AlarmCodes.ExitFailed).ShouldBeTrue();
        rig.OrdersOf(LegKind.Exit).ShouldBe(0, "an exit on top of a live target and stop could sell the shares twice");
    }

    [Fact]
    public async Task The_stop_and_the_exit_filling_in_the_same_second_is_oversold_with_a_freeze_request_and_nothing_sent_to_correct_it()
    {
        var rig = new Rig();
        await rig.FilledAsync();
        await rig.StopAsync();
        ToIst(rig, 15, 15);
        rig.Source.Last = 1480m;
        await rig.SquareOff.TickAsync(CancellationToken.None);
        rig.Broker.SetPrice("1594", 1480m); // both fill
        (await rig.Broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty.ShouldBe(-10);
        var calls = rig.Broker.Calls;

        await rig.Dog.TickAsync(CancellationToken.None);
        await rig.SquareOff.TickAsync(CancellationToken.None);
        await rig.Dog.TickAsync(CancellationToken.None);

        rig.IsOpen(AlarmCodes.Oversold).ShouldBeTrue();
        rig.Halts.Requests.ShouldContain(r => r.Level == HaltLevel.Freeze);
        rig.Broker.Calls.ShouldBe(calls, "no corrective order");
        rig.Events.State(361).ShouldNotBe(ExecState.Reconciled);
    }

    [Fact]
    public async Task Over_1000_seeds_the_square_off_ends_flat_or_with_the_oversold_alarm_and_the_freeze_standing_and_never_sells_more_without_saying_so()
    {
        var flat = 0;
        var oversold = 0;
        for (var seed = 0; seed < 1000; seed++)
        {
            var rnd = new Random(seed);
            var rig = new Rig();
            await rig.FilledAsync();
            await rig.StopAsync();
            ToIst(rig, 15, 14, 55);
            var price = 1480m + (rnd.Next(0, 30) * 0.5m);
            for (var i = 0; i < 40; i++)
            {
                price = Math.Max(1400m, price + (rnd.Next(-6, 7) * 0.5m));
                rig.Source.Last = price;
                switch (rnd.Next(0, 3))
                {
                    case 0:
                        rig.Broker.SetPrice("1594", price);
                        break;
                    case 1:
                        await rig.SquareOff.TickAsync(CancellationToken.None);
                        break;
                    default:
                        await rig.Dog.TickAsync(CancellationToken.None);
                        break;
                }

                rig.Clock.Advance(TimeSpan.FromSeconds(1));
            }

            for (var i = 0; i < 60; i++)
            {
                rig.Source.Last = price;
                rig.Broker.SetPrice("1594", price);
                await rig.SquareOff.TickAsync(CancellationToken.None);
                await rig.Dog.TickAsync(CancellationToken.None);
                rig.Clock.Advance(TimeSpan.FromSeconds(1));
            }

            var net = (await rig.Broker.GetPositionsAsync(CancellationToken.None)).Value!.Single().NetQty;
            if (net == 0)
            {
                flat++;
                continue;
            }

            net.ShouldBeLessThan(0, $"seed {seed}: a position left open after the square-off");
            rig.IsOpen(AlarmCodes.Oversold).ShouldBeTrue($"seed {seed}");
            rig.Halts.Requests.ShouldContain(r => r.Level == HaltLevel.Freeze, $"seed {seed}");
            rig.Events.State(361).ShouldNotBe(ExecState.Reconciled);
            oversold++;
        }

        (flat + oversold).ShouldBe(1000);
        flat.ShouldBeGreaterThan(0);
        oversold.ShouldBeGreaterThan(0, "the seeds do reach the race");
    }

    [Fact]
    public async Task A_working_entry_is_cancelled_at_the_entry_window_end_and_never_fills_when_the_price_reaches_it_later()
    {
        var rig = new Rig();
        await rig.Broker.PlaceAsync(Rig.Entry(), CancellationToken.None);
        rig.Source.NetOverride = 0;
        ToIst(rig, 14, 59);
        await rig.SquareOff.TickAsync(CancellationToken.None);
        rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1)).ShouldBe("PENDING");

        ToIst(rig, 15, 0);
        await rig.SquareOff.TickAsync(CancellationToken.None);
        rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1)).ShouldBe("CANCELLED");

        ToIst(rig, 15, 16);
        rig.Broker.SetPrice("1594", 1500m);
        rig.Broker.FilledOf(Rig.Cid(LegKind.Entry, 1)).ShouldBe(0);
    }

    [Fact]
    public async Task A_part_filled_entry_loses_its_remainder_and_the_filled_part_is_protected()
    {
        var rig = new Rig();
        rig.Broker.MaxFillPerStep = 4;
        await rig.Broker.PlaceAsync(Rig.Entry(), CancellationToken.None);
        rig.Broker.SetPrice("1594", 1500m);
        rig.Source.RaisedAt = rig.Clock.GetUtcNow();
        ToIst(rig, 15, 0);
        await rig.SquareOff.TickAsync(CancellationToken.None);
        rig.Broker.SetPrice("1594", 1500m);

        rig.Broker.FilledOf(Rig.Cid(LegKind.Entry, 1)).ShouldBe(4);
        rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1)).ShouldBe("CANCELLED");
        rig.Source.RaisedAt = rig.Clock.GetUtcNow();
        await rig.TickAsync(12);
        rig.Broker.Legs.Single(l => l.Kind == LegKind.Stop).Qty.ShouldBe(4);
    }

    [Fact]
    public async Task An_entry_that_survives_the_cancel_until_15_15_is_exit_failed_and_no_exit_is_sent_on_top_of_it()
    {
        var rig = new Rig();
        await rig.FilledAsync(4);
        await rig.StopAsync(4);
        await rig.Broker.PlaceAsync(Rig.Entry(6) with { ClientOrderId = Rig.Cid(LegKind.Entry, 1, 362) }, CancellationToken.None);
        rig.Source.EntryIntentOverride = 361;
        ToIst(rig, 15, 0);
        rig.Broker.RejectNextWith = "rejected";
        await rig.SquareOff.TickAsync(CancellationToken.None);
        ToIst(rig, 15, 15);
        rig.Source.Last = 1500m;
        await rig.SquareOff.TickAsync(CancellationToken.None);

        rig.IsOpen(AlarmCodes.ExitFailed).ShouldBeTrue();
        rig.Alarms.Raised.Single(a => a.Code == AlarmCodes.ExitFailed).Severity.ShouldBe(AlarmSeverity.Critical);
        rig.OrdersOf(LegKind.Exit).ShouldBe(0);
    }

    [Fact]
    public async Task Over_1000_seeds_no_entry_fills_after_the_square_off()
    {
        for (var seed = 0; seed < 1000; seed++)
        {
            var rnd = new Random(seed);
            var rig = new Rig();
            rig.Broker.MaxFillPerStep = rnd.Next(0, 8);
            await rig.Broker.PlaceAsync(Rig.Entry(), CancellationToken.None);
            rig.Source.NetOverride = 0;
            var failFirstCancel = rnd.Next(0, 3) == 0;
            ToIst(rig, 14, 50);
            for (var minute = 50; minute < 75; minute++)
            {
                ToIst(rig, 14 + (minute / 60), minute % 60);
                if (rnd.Next(0, 4) == 0 && minute < 60)
                {
                    rig.Broker.SetPrice("1594", 1500m);
                }

                if (failFirstCancel && minute == 60)
                {
                    rig.Broker.RejectNextWith = "rejected";
                }

                await rig.SquareOff.TickAsync(CancellationToken.None);
            }

            var filledAtClose = rig.Broker.FilledOf(Rig.Cid(LegKind.Entry, 1));
            rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1)).ShouldNotBe("PENDING", $"seed {seed}");
            rig.Broker.StatusOf(Rig.Cid(LegKind.Entry, 1)).ShouldNotBe("PART_TRADED", $"seed {seed}");
            ToIst(rig, 15, 16);
            rig.Broker.SetPrice("1594", 1500m);
            rig.Broker.FilledOf(Rig.Cid(LegKind.Entry, 1)).ShouldBe(filledAtClose, $"seed {seed}: a fill after the square-off");
        }
    }
}
