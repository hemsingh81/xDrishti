using XDrishti.Simulation.Running;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The engine's position gate, run on the same candidates and minutes as the oracle's gate in the author's gate rows (G1, G2, G2b, G3, O9 and the hold-end edge): the blocked set, each hold end and every walk must be the oracle's, so that the
/// rows of the spec that are written as tests and not as data are also three-way (the author's expectations in <c>GateTests</c>, the oracle, the engine).
/// </summary>
internal static class EngineGate
{
    internal static readonly RatioStats Stats = new();

    internal static void ShouldAgree(OracleGateResult oracle, IReadOnlyList<OracleBar> bars, IReadOnlyList<OracleCandidate> candidates, Func<OracleCandidate, OracleRules> rulesFor)
    {
        var engineBars = OracleAdapter.Bars(bars);
        var engineCandidates = candidates.Select(OracleAdapter.Candidate).ToList();
        var byEngine = engineCandidates.Zip(candidates).ToDictionary(p => p.First, p => p.Second);
        var gated = PositionGate.Apply(engineCandidates, c => TradeWalker.Walk(c, engineBars, OracleAdapter.Rules(rulesFor(byEngine[c]))));
        gated.IsSuccess.ShouldBeTrue("the engine's gate failed");

        gated.Value.Count.ShouldBe(oracle.Entries.Count);
        for (var i = 0; i < oracle.Entries.Count; i++)
        {
            var engine = OracleAdapter.Result(gated.Value[i].Walk);
            (engine.NoTrade == OracleNoTradeReason.PositionOpen).ShouldBe(oracle.Entries[i].Blocked, $"candidate {i}: blocked");
            engine.HoldEndEpochSeconds.ShouldBe(oracle.Entries[i].HoldEndEpochSeconds, $"candidate {i}: hold end");
            var differences = OracleComparer.Diff(oracle.Entries[i].Result, engine, Stats, $"gate row candidate {i}");
            differences.ShouldBeEmpty($"candidate {i}: " + string.Join("; ", differences));
        }
    }
}
