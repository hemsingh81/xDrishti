using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>P3-04d invariants over every built-in strategy: a read never reaches beyond the history its condition keeps, and the explanation of every trade of seeded series agrees with the trade (setup true, trigger true, invalidate not true).</summary>
public sealed class ExplainConsistencyTests
{
    public static TheoryData<string> Strategies()
    {
        var data = new TheoryData<string>();
        foreach (var info in BuiltInStrategies.Catalog)
        {
            data.Add(info.Id);
        }

        return data;
    }

    private static PreparedStrategy Prepared(string id)
    {
        var info = BuiltInStrategies.Catalog.Single(b => b.Id == id);
        var build = BuiltInStrategies.Prepare(id, null, info.DefaultTimeframe);
        build.Strategy.ShouldNotBeNull(string.Join("; ", build.Problems.Select(p => p.Path + " " + p.Problem.Code)));
        return build.Strategy;
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public void Every_read_of_every_condition_is_within_the_history_the_condition_keeps(string id)
    {
        var strategy = Prepared(id);

        foreach (var side in strategy.Simulator.Sides.Select(s => strategy.Simulator.CompiledOf(s)!))
        {
            foreach (var item in side.Items)
            {
                var condition = item.Condition;
                condition.Reads.ShouldAllBe(r => r.Lag >= 0 && r.Lag < condition.HistoryDepth, id + " " + item.Path);
                condition.Reads.ShouldAllBe(r => r.FeatureIndex >= 0 && r.FeatureIndex < condition.Features.Count, id + " " + item.Path);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Strategies))]
    public void The_explanation_of_every_trade_of_the_seeded_series_agrees_with_the_trade(string id)
    {
        var strategy = Prepared(id);
        var explained = 0;
        foreach (var series in SeededReplay.All.Take(25))
        {
            var from = series.Dates[0];
            var to = series.Dates[^1];
            var input = Rep.Input(series.Bars, from, to);
            var plain = InstrumentReplayer.Replay(strategy, input);
            var requests = plain.Trades.Concat(plain.OpenAtEnd).Select(t => new ExplainRequest(t.Key, t.DecisionEpochSeconds, t.Side)).ToList();

            var answer = InstrumentReplayer.Explain(strategy, input, requests);

            answer.Replay.Trades.Select(RunText.Trade).ShouldBe(plain.Trades.Select(RunText.Trade));
            answer.ByTradeKey.Keys.Order(StringComparer.Ordinal).ShouldBe(requests.Select(r => r.TradeKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            foreach (var explanation in answer.ByTradeKey.Values)
            {
                explained++;
                foreach (var section in explanation.Sections)
                {
                    var truths = section.Items.Select(i => i.Truth).ToList();
                    var holds = section.SectionName == "invalidate"
                        ? !(section.Mode == "any" ? truths.Contains(TruthState.True) : truths.All(t => t == TruthState.True))
                        : section.Mode == "any" ? truths.Contains(TruthState.True) : truths.All(t => t == TruthState.True);
                    holds.ShouldBeTrue(id + " " + section.SectionName);
                    section.Items.ShouldAllBe(i => i.Reads.Count == 0 || i.Reads.Any(r => r.Value != null) || i.Truth == TruthState.Unknown);
                }
            }
        }

        if (id == "ORB")
        {
            explained.ShouldBeGreaterThan(0, "the seeded series make ORB trades, so the loop above checked something");
        }
    }
}
