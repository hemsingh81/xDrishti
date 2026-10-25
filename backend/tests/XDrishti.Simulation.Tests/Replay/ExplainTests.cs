using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>Criteria 1 to 3 and 5 of P3-04d at the engine: the explanation of a trade is read from the replay's own trace of the decision.</summary>
public sealed class ExplainTests
{
    private static readonly PreparedStrategy WithEma = Rep.Prepare(Rep.OrbWithEma10());

    private static readonly PreparedStrategy Bare = Rep.Prepare(OrbV1.Create());

    private static ExplainRequest Ask(SimTrade trade) => new(trade.Key, trade.DecisionEpochSeconds, trade.Side);

    private static ExplainResult Explain(PreparedStrategy strategy, Bar[] bars, params ExplainRequest[] requests) =>
        InstrumentReplayer.Explain(strategy, Rep.Input(bars, ReplayDays.D1, ReplayDays.D3), requests);

    private static Bar[] ThreeDays(double factor = 1.0)
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(ReplayDays.D1, 99.00), ReplayDays.F1(ReplayDays.D2), ReplayDays.Flat(ReplayDays.D3, 105.00));
        return factor == 1.0 ? bars : [.. bars.Select(b => b with { Open = b.Open * factor, High = b.High * factor, Low = b.Low * factor, Close = b.Close * factor })];
    }

    [Fact]
    public void The_replay_of_an_explain_is_the_replay_of_the_same_input()
    {
        var bars = ThreeDays();
        var alone = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3).Trades.Single();

        var explained = Explain(Bare, bars, Ask(alone));

        RunText.Trade(explained.Replay.Trades.Single()).ShouldBe(RunText.Trade(alone));
    }

    [Fact]
    public void The_trigger_of_orb_is_true_with_the_four_reads_at_their_lags_and_the_values_of_the_decision_bar()
    {
        var bars = ThreeDays();
        var trade = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3).Trades.Single();

        var explanation = Explain(Bare, bars, Ask(trade)).ByTradeKey[trade.Key];

        explanation.DecisionEpochSeconds.ShouldBe(trade.DecisionEpochSeconds);
        explanation.Timeframe.ShouldBe(Timeframe.M15);
        var section = explanation.Sections.ShouldHaveSingleItem();
        section.SectionName.ShouldBe("trigger");
        section.Mode.ShouldBe("all");
        var item = section.Items.ShouldHaveSingleItem();
        item.Truth.ShouldBe(TruthState.True);
        item.Reads.Select(r => (r.Name, r.Lag)).ShouldBe([("close", 0), ("or_high(15)", 0), ("close", 1), ("or_high(15)", 0)]);
        item.Reads[0].Value.ShouldBe(trade.TriggerBar.Close);
        item.Reads[0].Value!.Value.ShouldBeGreaterThan(item.Reads[1].Value!.Value);
        item.Reads[2].Value!.Value.ShouldBeLessThanOrEqualTo(item.Reads[3].Value!.Value);
        item.Reads[1].Value.ShouldBe(item.Reads[3].Value);
    }

    [Fact]
    public void A_side_with_a_setup_lists_the_setup_first_and_it_is_true_at_the_decision()
    {
        var bars = ThreeDays();
        var trade = Rep.Replay(WithEma, bars, ReplayDays.D1, ReplayDays.D3).Trades.First(t => t.TradeDate == ReplayDays.D2);

        var explanation = Explain(WithEma, bars, Ask(trade)).ByTradeKey[trade.Key];

        explanation.Sections.Select(s => s.SectionName).ShouldBe(["setup", "trigger"]);
        explanation.Sections[0].Items.ShouldHaveSingleItem().Truth.ShouldBe(TruthState.True);
        explanation.Sections[0].Items[0].Reads.Select(r => r.Name).ShouldBe(["close", "ema(10)"]);
    }

    [Fact]
    public void A_request_that_is_not_a_decision_has_no_entry()
    {
        var bars = ThreeDays();
        var trade = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3).Trades.Single();

        var explained = Explain(Bare, bars, new ExplainRequest(trade.Key, trade.DecisionEpochSeconds + 900, trade.Side), new ExplainRequest("other", trade.DecisionEpochSeconds, Side.Sell));

        explained.ByTradeKey.ShouldBeEmpty();
    }

    [Fact]
    public void The_values_come_from_the_frame_so_other_bars_give_other_values()
    {
        var bars = ThreeDays();
        var trade = Rep.Replay(Bare, bars, ReplayDays.D1, ReplayDays.D3).Trades.Single();
        var doubled = ThreeDays(2.0);
        var doubledTrade = Rep.Replay(Bare, doubled, ReplayDays.D1, ReplayDays.D3).Trades.Single();

        var one = Explain(Bare, bars, Ask(trade)).ByTradeKey[trade.Key];
        var two = Explain(Bare, doubled, Ask(doubledTrade)).ByTradeKey[doubledTrade.Key];

        two.Sections[0].Items[0].Reads[0].Value.ShouldBe(one.Sections[0].Items[0].Reads[0].Value * 2);
    }
}
