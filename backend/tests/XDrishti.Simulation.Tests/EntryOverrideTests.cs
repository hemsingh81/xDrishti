using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// CF-07, criterion 2 (ADR 0208): the entry-override seam. A forced entry at the minute and the stop the strategy itself used reproduces the replayed trade; a forced entry never reads a minute after its exit; the stop of a forced entry is the given
/// fraction of the trigger close, below it for a buy and above it for a sell.
/// </summary>
public sealed class EntryOverrideTests
{
    private static readonly DateOnly Date = ReplayDays.D1;

    private static readonly WalkRules Rules = Cand.Standard;

    private static Bar[] F1() => ReplayDays.F1(Date);

    [Fact]
    public void A_forced_entry_at_the_minute_and_the_stop_the_strategy_used_reproduces_the_replayed_trade()
    {
        var bars = F1();
        var replayed = Rep.Replay(Rep.Prepare(OrbV1.Create()), bars, Date, Date).Trades.ShouldHaveSingleItem();
        var index = Array.FindIndex(bars, b => b.EpochSeconds == replayed.DecisionEpochSeconds - 60);

        var forced = EntryOverride.Walk(EntryOverride.Forced(Cand.Test, "ORB", bars, index, Side.Buy, replayed.StopPrice), bars, Rules).Value.Trade.ShouldNotBeNull();

        // only the trigger bar differs: the strategy decided on a 15-minute bar, the forced decision is the minute itself
        (forced.EntryEpochSeconds, forced.EntryPrice, forced.StopPrice, forced.TargetPrice, forced.ExitEpochSeconds, forced.ExitPrice, forced.ExitReason, forced.RExact, forced.RFilled, forced.MaeR, forced.MfeR)
            .ShouldBe((replayed.EntryEpochSeconds, replayed.EntryPrice, replayed.StopPrice, replayed.TargetPrice, replayed.ExitEpochSeconds, replayed.ExitPrice, replayed.ExitReason, replayed.RExact, replayed.RFilled, replayed.MaeR, replayed.MfeR));
        forced.DecisionEpochSeconds.ShouldBe(replayed.DecisionEpochSeconds);
    }

    [Fact]
    public void A_forced_entry_never_reads_a_minute_after_its_exit()
    {
        var bars = F1();
        var candidate = EntryOverride.Forced(Cand.Test, "ORB", bars, 29, Side.Buy, 98.00);
        var full = EntryOverride.Walk(candidate, bars, Rules).Value.Trade.ShouldNotBeNull();
        var exitIndex = Array.FindIndex(bars, b => b.EpochSeconds == full.ExitEpochSeconds);

        var cut = EntryOverride.Walk(candidate, bars[..(exitIndex + 1)], Rules).Value.Trade;
        var scrambled = EntryOverride.Walk(candidate, [.. bars[..(exitIndex + 1)], .. bars[(exitIndex + 1)..].Select(b => b with { Open = 1, High = 1_000, Low = 0.01, Close = 1 })], Rules).Value.Trade;

        cut.ShouldBe(full, "the minutes after the exit are not there");
        scrambled.ShouldBe(full, "and whatever they hold changes nothing");
    }

    [Fact]
    public void The_stop_is_the_risk_fraction_of_the_close_below_it_for_a_buy_and_above_it_for_a_sell()
    {
        EntryOverride.StopAt(Side.Buy, 200, 0.01).ShouldBe(198.0, 1e-9);
        EntryOverride.StopAt(Side.Sell, 200, 0.01).ShouldBe(202.0, 1e-9);
    }

    [Fact]
    public void A_decision_outside_the_bars_is_refused_by_the_seam()
    {
        var bars = F1();

        Should.Throw<ArgumentOutOfRangeException>(() => EntryOverride.Forced(Cand.Test, "ORB", bars, -1, Side.Buy, 98));
        Should.Throw<ArgumentOutOfRangeException>(() => EntryOverride.Forced(Cand.Test, "ORB", bars, bars.Length, Side.Buy, 98));
    }
}
