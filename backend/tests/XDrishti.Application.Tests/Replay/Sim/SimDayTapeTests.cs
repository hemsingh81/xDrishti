#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Sim;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Replay.Sim;

/// <summary>SR-04 part B (ADR 0293): the tape of a settled day. AC 5: replaying it minute by minute, with any paging, gives the settled day's trades and equity. AC 6: the page up to minute m holds no bar or event after m, and its board uses only them.</summary>
public sealed class SimDayTapeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ReplayWorld World()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB", "CCC", "DDD");
        world.Rising("AAA", 0.7);
        world.Rising("BBB", 2.0);
        world.Rising("CCC", 3.3);
        world.Rising("DDD", 4.9);
        return world;
    }

    private static async Task<(SimDaysRig Rig, Guid Id)> SettledRunAsync(int sessions)
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(sessions);
        (await new SetSimAutoHandler(rig.Store, rig.Calendar, rig.Options, rig.Time).HandleAsync(new SetSimAutoCommand(id, true), Ct)).IsSuccess.ShouldBeTrue();
        await rig.Runner().TickAsync(Ct);
        rig.Store.Row(id).Phase.ShouldBe(SimDays.Complete);
        return (rig, id);
    }

    private static GetSimDayTapeHandler Tape(SimDaysRig rig) => new(rig.Store, ReplayRig.Create(rig.World).Facts);

    private static async Task<SimDayTapeView> PageAsync(SimDaysRig rig, Guid id, DateOnly day, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var page = await Tape(rig).HandleAsync(new GetSimDayTapeQuery(id, day, from, to), Ct);
        page.IsSuccess.ShouldBeTrue(page.IsFailure ? page.Error.Message : string.Empty);
        return page.Value;
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(7, 1)]
    [InlineData(60, 2)]
    [InlineData(375, 3)]
    public async Task Paging_the_tape_minute_by_minute_with_any_page_size_rebuilds_exactly_the_settled_days_trades_and_equity(int pageMinutes, int seed)
    {
        var (rig, id) = await SettledRunAsync(8);
        var random = new Random(seed);
        var stored = rig.Store.Days[id];
        var eventsSeen = 0;

        foreach (var day in stored)
        {
            var settled = ReplayJson.Read<List<SimDayTrade>>(day.TradesJson)!;
            var events = new List<SimTapeEvent>();
            var bars = new List<SimTapeBar>();
            SimBoard? board = null;
            for (var from = SimTape.MarketOpen(day.Session); from <= SimTape.MarketClose(day.Session);)
            {
                var size = Math.Max(1, pageMinutes + random.Next(-pageMinutes / 2, pageMinutes / 2 + 1)); // uneven pages, as pauses and speed changes give
                var to = from.AddMinutes(size - 1) < SimTape.MarketClose(day.Session) ? from.AddMinutes(size - 1) : SimTape.MarketClose(day.Session);
                var page = await PageAsync(rig, id, day.Session, from, to);
                page.Bars.ShouldAllBe(b => b.At >= from && b.At <= to);
                page.Events.ShouldAllBe(e => e.At >= from && e.At <= to);
                events.AddRange(page.Events);
                bars.AddRange(page.Bars);
                board = page.Board;
                SimTape.BoardAt(to, page.Board.EquityOpen, settled, bars).Equity.ShouldBe(page.Board.Equity, "the pure board over what was streamed equals the served board");
                from = to.AddMinutes(1);
            }

            board!.Equity.ShouldBe(day.EquityClose, "animated ends where Instant ended");
            events.Where(e => e.Kind == "entry").Select(e => (e.TradeNo, e.Price)).ShouldBe(settled.Where(t => t.EntrySession == day.Session).OrderBy(t => t.TradeNo).Select(t => (t.TradeNo, (decimal?)t.EntryPrice)), ignoreOrder: true);
            events.Where(e => e.Kind == "exit").Select(e => (e.TradeNo, e.GrossPnl)).ShouldBe(settled.Where(t => t.ExitSession == day.Session).Select(t => (t.TradeNo, t.GrossPnl)), ignoreOrder: true);
            eventsSeen += events.Count;
        }

        eventsSeen.ShouldBeGreaterThan(0, "the parity is not of empty days");
        stored.Select(d => d.EquityClose).Last().ShouldBe(rig.Store.Row(id).LastEquity!.Value);
    }

    [Fact]
    public async Task A_page_up_to_minute_m_reads_and_serves_no_bar_after_m_and_its_board_shows_the_positions_open_then()
    {
        var (rig, id) = await SettledRunAsync(8);
        var day = rig.Store.Days[id].First(d => ReplayJson.Read<List<SimDayTrade>>(d.TradesJson)!.Any(t => t.EntrySession == d.Session));
        var entered = ReplayJson.Read<List<SimDayTrade>>(day.TradesJson)!.First(t => t.EntrySession == day.Session);
        var before = SimTape.Minute(entered.EntryAt).AddMinutes(-1);
        rig.World.MinuteReadEnds.Clear();

        var early = await PageAsync(rig, id, day.Session, before.AddMinutes(-5), before); // the fills of the invented market are at 09:15, so the minute before is pre-open
        var reads = rig.World.MinuteReadEnds.ToList();
        var late = await PageAsync(rig, id, day.Session, SimTape.Minute(entered.EntryAt), SimTape.MarketClose(day.Session));

        reads.ShouldAllBe(end => end <= before.AddSeconds(59), "no bar after the clock minute is read");
        early.Board.Open.ShouldNotContain(p => p.TradeNo == entered.TradeNo);
        early.Events.ShouldNotContain(e => e.TradeNo == entered.TradeNo);
        var position = late.Board.Open.Concat(late.Events.Where(e => e.Kind == "exit").Select(e => new SimBoardPosition(e.TradeNo, e.Symbol, e.Side, e.Qty, entered.EntryAt, entered.EntryPrice, e.Stop, null, 0))).First(p => p.TradeNo == entered.TradeNo);
        (position.EntryPrice, position.Stop, position.Qty).ShouldBe((entered.EntryPrice, entered.StopPrice, entered.Qty));
        late.Symbols.ShouldContain(entered.Symbol);
    }

    [Fact]
    public void The_board_marks_an_open_position_at_the_last_bar_up_to_the_minute_and_counts_only_the_days_exits()
    {
        var day = new DateOnly(2026, 10, 6);
        var at = (int h, int m) => new DateTimeOffset(day.ToDateTime(new TimeOnly(h, m)), IndiaTime.Offset);
        SimDayTrade[] trades =
        [
            new(1, "AAA", "BUY", "c", 10, day, at(9, 20), 100m, 98m, false, day, at(10, 0), 30m, 1.5, ExitPrice: 103m),
            new(2, "BBB", "SELL", "c", 5, day, at(9, 30), 200m, 204m, true, null, null, null, null),
        ];
        SimTapeBar[] bars = [new("BBB", at(9, 45), 0, 0, 0, 198, 1), new("BBB", at(11, 0), 0, 0, 0, 190, 1)];

        var mid = SimTape.BoardAt(at(9, 50), 1_000m, trades, bars);
        var end = SimTape.BoardAt(at(15, 29), 1_000m, trades, bars);

        mid.Open.Select(p => (p.TradeNo, p.LastPrice, p.RunningPnl)).ShouldBe([(1L, (decimal?)null, 0m), (2L, 198m, 10m)], "a later bar is never used");
        (mid.Realised, mid.Equity).ShouldBe((0m, 1_000m));
        end.Open.Single().RunningPnl.ShouldBe(50m);
        (end.Realised, end.Equity).ShouldBe((30m, 1_030m));
    }

    [Fact]
    public async Task An_unknown_run_is_not_found_an_unsettled_cursor_day_is_not_settled_and_a_page_off_the_day_is_invalid()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(3);
        var cursor = rig.Store.Row(id).Cursor;

        var unknown = await Tape(rig).HandleAsync(new GetSimDayTapeQuery(Guid.NewGuid(), cursor, null, null), Ct);
        var unsettled = await Tape(rig).HandleAsync(new GetSimDayTapeQuery(id, cursor, null, null), Ct);
        var never = await Tape(rig).HandleAsync(new GetSimDayTapeQuery(id, cursor.AddDays(40), null, null), Ct);
        var off = await Tape(rig).HandleAsync(new GetSimDayTapeQuery(id, cursor, SimTape.MarketOpen(cursor.AddDays(1)), null), Ct);

        unknown.Error.Code.ShouldBe(SimDays.NotFoundCode);
        unsettled.Error.Code.ShouldBe(SimDays.NotSettledCode);
        never.Error.Code.ShouldBe("sim.day_not_found");
        off.Error.Code.ShouldBe(SimDays.InvalidCode);
    }

    [Fact]
    public async Task A_run_inside_the_hold_out_whose_facts_cannot_widen_the_clamp_fails_with_a_reason_instead_of_settling()
    {
        var rig = new SimDaysRig(World());
        var id = await rig.StartAsync(3, readThrough: ReplayWorld.RunSessions[2]);
        var facts = Substitute.For<IReplayFacts>(); // a facts source without a clamp of its own: ReadInsideHoldOut is false

        var opened = await SimDayBook.OpenAsync(ReplayRig.Create(rig.World).Driver, facts, rig.Store.Row(id), Ct);

        opened.IsFailure.ShouldBeTrue();
        opened.Error.Code.ShouldBe("sim.holdout");
        await facts.DidNotReceive().CalendarAsync(Arg.Any<CancellationToken>());
    }
}
