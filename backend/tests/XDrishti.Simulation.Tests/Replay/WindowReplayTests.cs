using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Criteria 1 to 3 of P3-04: the replay starts at the first stored session whatever <c>From</c> is, only the trades are cut to the window, and a trade without a later minute is not a statistic.</summary>
public sealed class WindowReplayTests
{
    private static readonly PreparedStrategy WithEma = Rep.Prepare(Rep.OrbWithEma10());

    private static readonly PreparedStrategy Bare = Rep.Prepare(OrbV1.Create());

    private static Bar[] ThreeDays() => ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00));

    [Fact]
    public void The_trade_of_the_middle_day_is_the_same_for_a_window_of_that_day_and_for_the_whole_series()
    {
        var bars = ThreeDays();

        var narrow = Rep.Replay(WithEma, bars[..(2 * 375)], ReplayDays.D2, ReplayDays.D2);
        var wide = Rep.Replay(WithEma, bars, ReplayDays.D1, ReplayDays.D3);

        narrow.Trades.Count.ShouldBe(1);
        wide.Trades.Count.ShouldBe(1);
        RunText.Trade(narrow.Trades[0]).ShouldBe(RunText.Trade(wide.Trades[0]));
        var trade = wide.Trades[0];
        trade.TradeDate.ShouldBe(ReplayDays.D2);
        trade.EntryPrice.ShouldBe(100.50);
        trade.StopPrice.ShouldBe(98.00);
        trade.TargetPrice.ShouldBe(105.50);
        trade.ExitPrice.ShouldBe(105.50);
        trade.ExitReason.ShouldBe(ExitReason.Target);
        trade.RExact.ShouldBe(2.0);
        trade.RFilled.ShouldBe(2.0);
    }

    [Fact]
    public void The_control_fixture_can_fail_a_series_without_the_first_day_holds_no_trade_because_ema10_is_not_ready_in_time()
    {
        var bars = ThreeDays()[375..];

        var replay = Rep.Replay(WithEma, bars, ReplayDays.D2, ReplayDays.D3);

        replay.Trades.ShouldBeEmpty();
        replay.OpenAtEnd.ShouldBeEmpty();
        replay.Record.Scans[0].Candidates.ShouldBe(0);
        Identities.Of(replay.Record.Scans[0], "buy.setup[0]").NotReady.ShouldBe(9, "ema(10) on 15m is ready at the tenth closed bar (11:45) of the first day, after the last entry (11:30); the second day's own steps are ready");
    }

    [Fact]
    public void A_different_from_only_adds_or_removes_the_trades_outside_it_and_the_counters_do_not_move()
    {
        var bars = ReplayDays.Join(ReplayDays.F2(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00));

        var narrow = Rep.Replay(Bare, bars, ReplayDays.D2, ReplayDays.D3);
        var wide = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3);
        var first = Rep.Replay(Bare, bars[..375], ReplayDays.D1, ReplayDays.D1);

        narrow.Record.Scans.Select(Render.Counters).ShouldBe(wide.Record.Scans.Select(Render.Counters));
        OutcomeFormatter.Render(narrow.Record.Walk).ShouldBe(OutcomeFormatter.Render(wide.Record.Walk));
        (narrow.Record.BarsRead, narrow.Record.BarsFed, narrow.Record.BarsExcluded, narrow.Record.Sessions, narrow.Record.Notes.Count).ShouldBe((wide.Record.BarsRead, wide.Record.BarsFed, wide.Record.BarsExcluded, wide.Record.Sessions, wide.Record.Notes.Count));
        narrow.Trades.Select(t => t.ExitReason).ShouldBe([ExitReason.Target]);
        wide.Trades.Select(t => t.ExitReason).ShouldBe([ExitReason.Stop, ExitReason.Target]);
        wide.Trades.Select(t => t.RExact).ShouldBe([-1.0, 2.0]);
        narrow.Record.Fates.TradesBeforeWindow.ShouldBe(1);
        wide.Record.Fates.TradesBeforeWindow.ShouldBe(0);
        first.Trades.Select(t => t.RExact).ShouldBe([-1.0]);
        first.Trades[0].TradeDate.ShouldBe(ReplayDays.D1);
    }

    [Fact]
    public void A_different_to_only_adds_or_removes_the_sessions_after_it()
    {
        var bars = ReplayDays.Join(ReplayDays.F1(ReplayDays.D1), ReplayDays.F1(ReplayDays.D2), ReplayDays.F1(ReplayDays.D3));

        var shorter = Rep.Replay(Bare, bars[..(2 * 375)], ReplayDays.D1, ReplayDays.D2);
        var longer = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3);

        shorter.Trades.Select(t => t.TradeDate).ShouldBe([ReplayDays.D1, ReplayDays.D2]);
        longer.Trades.Select(t => t.TradeDate).ShouldBe([ReplayDays.D1, ReplayDays.D2, ReplayDays.D3]);
        RunText.Trade(longer.Trades[0]).ShouldBe(RunText.Trade(shorter.Trades[0]));
        RunText.Trade(longer.Trades[1]).ShouldBe(RunText.Trade(shorter.Trades[1]));
        shorter.Record.Sessions.Ran.ShouldBe(2);
        longer.Record.Sessions.Ran.ShouldBe(3);
    }

    [Fact]
    public void A_trade_without_a_later_minute_is_open_at_the_end_and_not_a_statistic()
    {
        var bars = ReplayDays.F6(ReplayDays.D1);
        bars.Length.ShouldBe(355, "09:15 to 15:09, 94.7 % of 375");

        var replay = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D1);

        replay.Trades.ShouldBeEmpty();
        var open = replay.OpenAtEnd.ShouldHaveSingleItem();
        open.ExitReason.ShouldBe(ExitReason.EndOfData);
        open.ExitPrice.ShouldBe(103.00);
        open.RExact.ShouldBe(1.0);
        Day.Clock(Convert.ToInt32(((open.ExitEpochSeconds + (330 * 60)) % 86_400) / 60)).ShouldBe("15:09");
        replay.Record.Sessions.Ran.ShouldBe(1);
        replay.Record.Fates.OpenAtEnd.ShouldBe(1);
        replay.Record.Fates.Trades.ShouldBe(0);
    }

    [Fact]
    public void The_same_open_trade_dated_before_from_is_a_trade_before_the_window_and_not_open_at_the_end()
    {
        var bars = ReplayDays.Join(ReplayDays.F6(ReplayDays.D1), ReplayDays.Flat(ReplayDays.D2, 103.00));

        var replay = Rep.Replay(Bare, bars, ReplayDays.D2, ReplayDays.D2);

        replay.Trades.ShouldBeEmpty();
        replay.OpenAtEnd.ShouldBeEmpty();
        replay.Record.Fates.TradesBeforeWindow.ShouldBe(1);
        replay.Record.Fates.OpenAtEnd.ShouldBe(0);
    }

    [Fact]
    public void The_decision_index_is_the_index_in_the_one_list_whatever_is_skipped_or_restarted()
    {
        // d1 flat; d2 a 60-bar evening-shaped session that does not run (short); d3 the F1 day; d4 the F1 day at twice the price (a flagged day that restarts the frame); d5 the F1 day again at that level.
        var bars = ReplayDays.Join(
            ReplayDays.Flat(ReplayDays.D1, 99.00),
            ReplayDays.Minutes(ReplayDays.D2, "18:00", 60),
            ReplayDays.F1(ReplayDays.D3),
            ReplayDays.Scaled(ReplayDays.F1(ReplayDays.D4), 2.0),
            ReplayDays.Scaled(ReplayDays.F1(ReplayDays.D5), 2.0));
        var seen = new List<(int Candidates, int Mismatches)>();

        var replay = InstrumentReplayer.ReplayCore(
            Bare,
            Rep.Input(bars, ReplayDays.D1, ReplayDays.D5),
            (list, result) =>
            {
                var mismatches = result.Outcomes.Count(o => list[o.Candidate.DecisionBarIndex].EpochSeconds != o.Candidate.DecisionEpochSeconds - 60);
                seen.Add((result.Outcomes.Count, mismatches));
            });

        seen.Count.ShouldBe(2, "one call of Simulator.Run per segment: the second segment starts at the flagged day d4");
        seen.Sum(s => s.Candidates).ShouldBeGreaterThan(0);
        seen.Sum(s => s.Mismatches).ShouldBe(0);
        replay.Record.Sessions.ShortSession.ShouldBe(1);
        replay.Record.Discontinuities.Select(d => d.Date).ShouldBe([ReplayDays.D4]);
        replay.Record.Walk.Candidates.ShouldBe(replay.Record.Scans.Sum(s => s.Candidates));
        replay.Record.Fates.EntriesSuppressed.ShouldBe(1);
    }
}
