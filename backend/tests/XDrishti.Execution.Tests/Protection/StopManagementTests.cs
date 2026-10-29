using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Tests.Protection;

/// <summary>Criteria 5 (the properties, with negative controls), 6 (break-even) and 7 (stop triggered, not filled) of P12-04.</summary>
public sealed class StopManagementTests
{
    private static async Task<Rig> ProtectedAsync(ProtectionSettings? settings = null, decimal last = 1510m)
    {
        var rig = new Rig(settings);
        await rig.FilledAsync();
        await rig.StopAsync();
        rig.Source.Last = last;
        return rig;
    }

    [Fact]
    public async Task Tightening_moves_the_trigger_up_and_the_limit_with_it_and_counts_one_modification()
    {
        var rig = await ProtectedAsync();

        (await rig.Stops.TightenAsync(Rig.Account, 361, 1490m, CancellationToken.None)).ShouldBe("ok");

        var stop = rig.Broker.Legs.Single(l => l.Kind == LegKind.Stop);
        (stop.Trigger, stop.Price).ShouldBe((1490m, 1482.55m));
        rig.Broker.ModificationsOf(stop.ClientOrderId).ShouldBe(1);
        rig.Events.Events(361).Count(e => e.Kind == ExecEventKind.ModifySent).ShouldBe(1);
    }

    [Theory]
    [InlineData(1480.0, "stop_loosen")]
    [InlineData(1485.0, "no_change")]
    [InlineData(1509.95, "too_close_to_last")]
    public async Task A_loosening_a_repeat_and_a_stop_at_the_last_price_are_refused_and_nothing_is_sent(double trigger, string reason)
    {
        var rig = await ProtectedAsync();
        var calls = rig.Broker.Calls;

        (await rig.Stops.TightenAsync(Rig.Account, 361, (decimal)trigger, CancellationToken.None)).ShouldBe(reason);

        rig.Broker.Calls.ShouldBe(calls);
    }

    [Fact]
    public async Task The_modification_cap_holds_per_intent()
    {
        var rig = await ProtectedAsync(ProtectionSettings.Default, last: 1600m);
        for (var i = 1; i <= 20; i++)
        {
            rig.Clock.Advance(TimeSpan.FromSeconds(1));
            (await rig.Stops.TightenAsync(Rig.Account, 361, 1485m + (i * 0.05m), CancellationToken.None)).ShouldBe("ok", $"modification {i}");
        }

        (await rig.Stops.TightenAsync(Rig.Account, 361, 1500m, CancellationToken.None)).ShouldBe("modification_cap");
    }

    [Fact]
    public async Task Tightening_is_not_done_under_freeze_nor_without_a_gate_nor_on_a_stale_book_nor_on_a_stop_that_is_not_ours()
    {
        var frozen = await ProtectedAsync();
        frozen.Source.Halt = HaltLevel.Freeze;
        (await frozen.Stops.TightenAsync(Rig.Account, 361, 1490m, CancellationToken.None)).ShouldBe("frozen");

        var closed = new Rig(mode: new ModeAnswer(ExecMode.SemiAuto, false));
        await closed.FilledAsync();
        await closed.StopAsync();
        closed.Source.Last = 1510m;
        (await closed.Stops.TightenAsync(Rig.Account, 361, 1490m, CancellationToken.None)).ShouldBe("gate_closed");

        var stale = await ProtectedAsync();
        stale.Source.BookAge = TimeSpan.FromSeconds(31);
        (await stale.Stops.TightenAsync(Rig.Account, 361, 1490m, CancellationToken.None)).ShouldBe("book_stale");

        var super = await ProtectedAsync();
        super.Source.BookOverride = [new BrokerOrderView("B1", null, "PENDING", "1594", ExecSide.Sell, 10, 0, 1485m, null, ProtectionBook.SuperStopLeg)];
        (await super.Stops.TightenAsync(Rig.Account, 361, 1490m, CancellationToken.None)).ShouldBe("not_our_stop");

        var live = new Rig(environment: ExecEnvironment.Live);
        await live.FilledAsync();
        await live.StopAsync();
        live.Source.Last = 1510m;
        (await live.Stops.TightenAsync(Rig.Account, 361, 1490m, CancellationToken.None)).ShouldBe("gate_closed");
    }

    [Fact]
    public async Task Break_even_is_off_by_default_and_moves_the_stop_to_entry_plus_one_tick_at_1R_only()
    {
        var off = await ProtectedAsync(last: 1600m);
        (await off.Stops.BreakevenAsync(Rig.Account, 361, CancellationToken.None)).ShouldBe("off");

        var on = ProtectionSettings.Default with { BreakevenAtR = 1m };
        var below = await ProtectedAsync(on, last: 1514.95m);
        (await below.Stops.BreakevenAsync(Rig.Account, 361, CancellationToken.None)).ShouldBe("not_reached");
        below.Broker.Legs.Single(l => l.Kind == LegKind.Stop).Trigger.ShouldBe(1485m);

        var reached = await ProtectedAsync(on, last: 1515m);
        (await reached.Stops.BreakevenAsync(Rig.Account, 361, CancellationToken.None)).ShouldBe("ok");
        reached.Broker.Legs.Single(l => l.Kind == LegKind.Stop).Trigger.ShouldBe(1500.05m);
    }

    [Fact]
    public async Task A_stop_through_its_limit_for_5_seconds_gets_one_marketable_limit_change_with_the_trigger_kept()
    {
        var rig = await ProtectedAsync(last: 1470m);
        rig.Broker.SetPrice("1594", 1470m); // triggered at 1,485, below the limit 1,477.55: open, unfilled
        rig.Broker.StatusOf(Rig.Cid(LegKind.Stop, 1)).ShouldBe("PENDING");

        (await rig.Stops.StopUnfilledAsync(Rig.Account, 361, CancellationToken.None)).ShouldBe("waiting");
        rig.Clock.Advance(TimeSpan.FromSeconds(4));
        (await rig.Stops.StopUnfilledAsync(Rig.Account, 361, CancellationToken.None)).ShouldBe("waiting");
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        var orders = rig.Broker.Legs.Count;
        (await rig.Stops.StopUnfilledAsync(Rig.Account, 361, CancellationToken.None)).ShouldBe("ok");

        var stop = rig.Broker.Legs.Single(l => l.Kind == LegKind.Stop);
        (stop.Trigger, stop.Price).ShouldBe((1485m, 1455.30m));
        rig.Broker.Legs.Count.ShouldBe(orders, "one order, no second sell");
        rig.Count(AlarmCodes.StopTriggeredUnfilled).ShouldBe(1);
        rig.Source.Last = 1450m; // still through the (new) limit: no second change for the same leg
        (await rig.Stops.StopUnfilledAsync(Rig.Account, 361, CancellationToken.None)).ShouldBe("already_repriced");
        rig.Broker.ModificationsOf(stop.ClientOrderId).ShouldBe(1);

        rig.Broker.SetPrice("1594", 1470m);
        rig.Broker.FilledOf(stop.ClientOrderId).ShouldBe(10, "now marketable, it fills");
    }

    [Fact]
    public async Task A_price_that_comes_back_inside_the_limit_resets_the_five_second_clock()
    {
        var rig = await ProtectedAsync(last: 1470m);
        await rig.Stops.StopUnfilledAsync(Rig.Account, 361, CancellationToken.None);
        rig.Clock.Advance(TimeSpan.FromSeconds(4));
        rig.Source.Last = 1490m;
        (await rig.Stops.StopUnfilledAsync(Rig.Account, 361, CancellationToken.None)).ShouldBe("not_beyond");
        rig.Source.Last = 1470m;
        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        (await rig.Stops.StopUnfilledAsync(Rig.Account, 361, CancellationToken.None)).ShouldBe("waiting");
    }

    // ---- criterion 5: properties over 5,000 seeds, each with a negative control ----

    private sealed record Sim(decimal Stop, int Mods, decimal Last, bool Loosened, bool CloseToLast, bool OverCap);

    // Random management requests against a rule. 'rule' decides whether a request is applied.
    private static Sim Tighten(int seed, Func<decimal, decimal, decimal?, int, int, string?> rule)
    {
        var rnd = new Random(seed);
        var stop = 1485m;
        var last = 1500m;
        var mods = 0;
        bool loosened = false, close = false, over = false;
        for (var i = 0; i < 60; i++)
        {
            last = Math.Max(1m, last + (rnd.Next(-40, 41) * 0.05m));
            var request = StopRules.FloorToTick(stop + (rnd.Next(-60, 61) * 0.05m));
            if (rule(stop, request, last, mods, 20) is not null)
            {
                continue;
            }

            loosened |= request < stop;
            close |= request > last - (2 * StopRules.Tick);
            stop = request;
            mods++;
            over |= mods > 20;
        }

        return new Sim(stop, mods, last, loosened, close, over);
    }

    [Fact]
    public void Over_5000_seeds_the_stop_never_loosens_never_comes_within_two_ticks_of_last_and_never_exceeds_the_cap()
    {
        for (var seed = 0; seed < 5000; seed++)
        {
            var sim = Tighten(seed, StopRules.CheckTighten);
            (sim.Loosened, sim.CloseToLast, sim.OverCap, sim.Mods <= 20).ShouldBe((false, false, false, true), $"seed {seed}");
        }
    }

    [Fact]
    public void Negative_control_a_rule_that_allows_loosening_is_caught_on_many_seeds()
    {
        string? Loosening(decimal current, decimal next, decimal? last, int mods, int cap) => mods >= cap ? "cap" : next > last - (2 * StopRules.Tick) ? "close" : null;

        Enumerable.Range(0, 5000).Count(seed => Tighten(seed, Loosening).Loosened).ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void Negative_control_a_rule_without_the_cap_or_the_distance_is_caught_on_many_seeds()
    {
        string? NoCap(decimal current, decimal next, decimal? last, int mods, int cap) => next < current ? "loosen" : null;

        Enumerable.Range(0, 5000).Count(seed => Tighten(seed, NoCap) is { OverCap: true } or { CloseToLast: true }).ShouldBeGreaterThanOrEqualTo(10);
    }

    // Exits: random requests against a position; the sum in flight must never exceed q and none is sent at q = 0.
    private static (bool Exceeded, bool SentAtZero) Exits(int seed, Func<int, int, int> allowed)
    {
        var rnd = new Random(seed);
        var q = rnd.Next(0, 40);
        var inFlight = 0;
        bool exceeded = false, atZero = false;
        for (var i = 0; i < 40; i++)
        {
            if (rnd.Next(0, 4) == 0 && inFlight > 0)
            {
                var filled = rnd.Next(1, inFlight + 1);
                inFlight -= filled;
                q -= filled;
                continue;
            }

            var send = Math.Min(rnd.Next(1, 50), allowed(q, inFlight));
            if (send <= 0)
            {
                continue;
            }

            atZero |= q <= 0;
            inFlight += send;
            exceeded |= inFlight > q;
        }

        return (exceeded, atZero);
    }

    [Fact]
    public void Over_5000_seeds_exits_in_flight_never_exceed_q_and_none_is_sent_at_zero()
    {
        for (var seed = 0; seed < 5000; seed++)
        {
            Exits(seed, StopRules.MaxExitQty).ShouldBe((false, false), $"seed {seed}");
        }
    }

    [Fact]
    public void Negative_control_a_rule_that_ignores_the_exits_in_flight_is_caught_on_many_seeds() =>
        Enumerable.Range(0, 5000).Count(seed => Exits(seed, (q, _) => Math.Max(0, q)).Exceeded).ShouldBeGreaterThanOrEqualTo(10);

    [Fact]
    public void Negative_control_a_rule_that_allows_an_exit_at_zero_is_caught_on_many_seeds() =>
        Enumerable.Range(0, 5000).Count(seed => Exits(seed, (q, inFlight) => q <= 0 ? 5 : StopRules.MaxExitQty(q, inFlight)).SentAtZero).ShouldBeGreaterThanOrEqualTo(10);
}
