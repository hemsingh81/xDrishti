using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Sim;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Sim;

/// <summary>SR-04 part A (ADR 0293): the day-by-day simulation over the SR-01 engine, through its runner and its handlers, on an invented market.</summary>
public sealed class SimDaysRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Cut = new(2026, 10, 8); // the fourth session

    private static ReplayWorld World()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB", "CCC", "DDD");
        world.Rising("AAA", 0.7);
        world.Rising("BBB", 2.0);
        world.Rising("CCC", 3.3);
        world.Rising("DDD", 4.9);
        return world;
    }

    private static async Task AutoToTheEnd(SimDaysRig rig, Guid id)
    {
        (await new SetSimAutoHandler(rig.Store, rig.Calendar, rig.Options, rig.Time).HandleAsync(new SetSimAutoCommand(id, true), Ct)).IsSuccess.ShouldBeTrue();
        await rig.Runner().TickAsync(Ct);
    }

    [Fact]
    public async Task Advance_before_the_day_is_settled_is_refused_and_after_it_stores_the_day_and_moves_to_the_next_session()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(5);
        var runner = rig.Runner();

        var early = await rig.Advance().HandleAsync(new AdvanceSimDayCommand(id), Ct);
        (await rig.Settle().HandleAsync(new SettleSimDayCommand(id), Ct)).Value.Phase.ShouldBe(SimDays.Settling);
        await runner.TickAsync(Ct);
        rig.Store.Row(id).Phase.ShouldBe(SimDays.Settled);
        var advanced = await rig.Advance().HandleAsync(new AdvanceSimDayCommand(id), Ct);
        var again = await rig.Advance().HandleAsync(new AdvanceSimDayCommand(id), Ct);

        early.Error.Code.ShouldBe(SimDays.NotSettledCode);
        advanced.Value.Cursor.ShouldBe(new DateOnly(2026, 10, 6));
        (advanced.Value.Phase, advanced.Value.DaysDone, advanced.Value.DaysTotal).ShouldBe((SimDays.Idle, 1, 5));
        rig.Store.Days[id].Single().Session.ShouldBe(ReplayWorld.Start);
        again.Error.Code.ShouldBe(SimDays.NotSettledCode, "a repeat stores nothing twice");
    }

    [Fact]
    public async Task Every_day_equals_the_SR01_session_with_the_same_inputs_and_the_run_completes_at_its_end()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(8);

        await AutoToTheEnd(rig, id);

        var golden = await rig.Sr01Async(8);
        rig.Store.Row(id).Phase.ShouldBe(SimDays.Complete);
        rig.Store.Days[id].Select(d => d.ResultHash).ShouldBe(golden.Select(ReplayHashes.Session));
        rig.Store.Days[id].Select(d => d.EquityClose).ShouldBe(golden.Select(g => g.Equity));
        golden.Sum(g => g.Taken.Count).ShouldBeGreaterThan(0, "the comparison is not of empty days");
    }

    [Fact]
    public async Task The_golden_holds_in_learning_mode_too()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(6, ReplayMode.Learning);

        await AutoToTheEnd(rig, id);

        rig.Store.Days[id].Select(d => d.ResultHash).ShouldBe((await rig.Sr01Async(6, ReplayMode.Learning)).Select(ReplayHashes.Session));
    }

    [Fact]
    public async Task Auto_run_pauses_after_the_current_day_manual_steps_go_in_between_and_it_resumes_from_the_stored_cursor()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(8);
        rig.Store.OnAdvance = r =>
        {
            if (r.DaysDone == 3)
            {
                rig.Store.Put(rig.Store.Row(id) with { Auto = false }); // the owner pauses while day 3 settles
            }
        };

        await AutoToTheEnd(rig, id);
        rig.Store.Row(id).DaysDone.ShouldBe(3);
        rig.Store.OnAdvance = null;
        await rig.Settle().HandleAsync(new SettleSimDayCommand(id), Ct);
        await rig.Runner().TickAsync(Ct);
        await rig.Advance().HandleAsync(new AdvanceSimDayCommand(id), Ct);
        await AutoToTheEnd(rig, id);

        rig.Store.Row(id).Phase.ShouldBe(SimDays.Complete);
        rig.Store.Days[id].Select(d => d.ResultHash).ShouldBe((await rig.Sr01Async(8)).Select(ReplayHashes.Session));
    }

    [Fact]
    public async Task After_a_restart_a_day_half_settled_is_settled_again_from_its_start_with_the_same_result()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(8);
        rig.Store.OnAdvance = r =>
        {
            if (r.DaysDone == 4)
            {
                rig.Store.Put(rig.Store.Row(id) with { Auto = false });
            }
        };
        await AutoToTheEnd(rig, id);
        await rig.Settle().HandleAsync(new SettleSimDayCommand(id), Ct);
        var first = rig.Runner();
        await first.TickAsync(Ct);
        var settled = rig.Store.Row(id).Pending!;

        rig.Store.Put(rig.Store.Row(id) with { Phase = SimDays.Settling, Pending = null }); // killed while the day settled: only the request is stored
        await rig.Runner().TickAsync(Ct); // a new process: it rebuilds the book from the four stored days

        rig.Store.Row(id).Pending!.ResultHash.ShouldBe(settled.ResultHash);
        rig.Store.Row(id).Pending!.ResultHash.ShouldBe(ReplayHashes.Session((await rig.Sr01Async(8))[4]));
    }

    [Fact]
    public async Task A_stored_day_that_no_longer_replays_the_same_ends_the_run_as_diverged()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(5);
        rig.Store.OnAdvance = r => rig.Store.Put(rig.Store.Row(id) with { Auto = r.DaysDone < 2 });
        await AutoToTheEnd(rig, id);
        rig.Store.Days[id][0] = rig.Store.Days[id][0] with { ResultHash = new string('0', 64) };

        await rig.Settle().HandleAsync(new SettleSimDayCommand(id), Ct);
        await rig.Runner().TickAsync(Ct);

        rig.Store.Row(id).Phase.ShouldBe(SimDays.Failed);
        rig.Store.Row(id).FailReason!.ShouldStartWith("sim.diverged");
    }

    [Fact]
    public async Task Continue_extends_the_end_and_the_stored_days_keep_their_hashes()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(5);
        await AutoToTheEnd(rig, id);
        var old = rig.Store.Days[id].Select(d => d.ResultHash).ToList();
        var oldEnd = rig.Store.Row(id).End;

        (await rig.Store.ExtendAsync(id, ReplayWorld.RunSessions[7], ReplayWorld.RunSessions[7], ReplayWorld.RunSessions[5], null, null, rig.Time.GetUtcNow(), Ct)).ShouldBeTrue();
        await rig.Runner().TickAsync(Ct); // auto stays on: the cursor resumes

        var days = rig.Store.Days[id];
        rig.Store.Row(id).Phase.ShouldBe(SimDays.Complete);
        days.Count.ShouldBe(8);
        days.Take(5).Select(d => d.ResultHash).ShouldBe(old, "nothing before the old end is recomputed");
        days.Take(5).ShouldAllBe(d => d.EndInForce == oldEnd);
        days.Skip(5).ShouldAllBe(d => d.EndInForce == ReplayWorld.RunSessions[7]);
    }

    [Fact]
    public async Task Bars_after_a_day_change_nothing_the_cursor_path_says_about_the_days_before_it()
    {
        var honestRig = new SimDaysRig(World());
        var honestId = await honestRig.StartAsync(8);
        await AutoToTheEnd(honestRig, honestId);
        var honest = honestRig.Store.Days[honestId].Where(d => d.Session < Cut).Select(d => d.ResultHash).ToList();
        honest.Count.ShouldBe(3);

        for (var seed = 1; seed <= 5; seed++)
        {
            var world = World();
            var random = new Random(seed);
            foreach (var series in world.Close.Values)
            {
                foreach (var day in series.Keys.Where(d => d > Cut).ToList())
                {
                    series[day] = 40 + (random.NextDouble() * 120);
                }
            }

            var rig = new SimDaysRig(world);
            var id = await rig.StartAsync(8);
            await AutoToTheEnd(rig, id);

            rig.Store.Days[id].Where(d => d.Session < Cut).Select(d => d.ResultHash).ShouldBe(honest, "seed " + seed);
            rig.Store.Days[id].Where(d => d.Session > Cut).Select(d => d.ResultHash).ShouldNotBe(honestRig.Store.Days[honestId].Where(d => d.Session > Cut).Select(d => d.ResultHash), "the doctoring reaches the later days");
        }
    }

    [Fact]
    public async Task Final_equity_is_the_capital_plus_the_trade_results_from_the_paper_ledger()
    {
        var rig = new SimDaysRig(Job.ReplayJobRig.SwingWorld()); // AAA is taken on the first day, held overnight and leaves on the sixth
        var id = await rig.StartAsync(8);

        await AutoToTheEnd(rig, id);

        var days = rig.Store.Days[id];
        var trades = days.SelectMany(d => ReplayJson.Read<List<SimDayTrade>>(d.TradesJson)!).Where(t => t.ExitSession is not null).DistinctBy(t => t.TradeNo).ToList();
        trades.Count.ShouldBeGreaterThan(0);
        days[^1].EquityClose.ShouldBe(1_000_000m + trades.Sum(t => t.GrossPnl!.Value));
        rig.Store.Row(id).LastEquity.ShouldBe(days[^1].EquityClose);
    }

    [Fact]
    public async Task A_cancel_stops_auto_run_and_the_stored_days_stay()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(8);
        rig.Store.OnAdvance = r =>
        {
            if (r.DaysDone == 2)
            {
                rig.Store.Put(rig.Store.Row(id) with { Phase = SimDays.Cancelled, Auto = false });
            }
        };

        await AutoToTheEnd(rig, id);

        rig.Store.Row(id).Phase.ShouldBe(SimDays.Cancelled);
        rig.Store.Days[id].Count.ShouldBe(2);
    }

    [Fact]
    public async Task In_market_hours_the_runner_waits_and_settles_nothing()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(3);
        rig.Time.SetUtcNow(new DateTimeOffset(2026, 11, 11, 5, 0, 0, TimeSpan.Zero)); // 10:30 IST on a Wednesday
        await rig.Settle().HandleAsync(new SettleSimDayCommand(id), Ct);
        var runner = rig.Runner();

        await runner.TickAsync(Ct);

        rig.Store.Row(id).Phase.ShouldBe(SimDays.Settling);
        runner.Waiting.ShouldNotBeNull();
    }
}
