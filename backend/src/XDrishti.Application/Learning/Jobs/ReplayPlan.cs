using XDrishti.Application.Research;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>
/// A baseline that can be replayed (spec P8-18 Part B): the stored facts, how the strategy is named in a request, the exits that reproduce the baseline's own definition, and the knobs offered. It makes the request of the baseline as it was, and the request of a candidate with
/// exactly ONE field changed; nothing else differs between the two.
/// </summary>
/// <param name="Baseline">The stored facts.</param>
/// <param name="Stored">The strategy is a stored version named by its hash (else a built-in named by its id).</param>
/// <param name="BaselineExits">The exits to pass so that the definition is the baseline's, or null when the strategy's own exits give it.</param>
/// <param name="Offers">The knobs offered.</param>
public sealed record ReplayPlan(ReplayBaseline Baseline, bool Stored, ExitChoice? BaselineExits, IReadOnlyList<KnobOffer> Offers)
{
    /// <summary>The request that made the baseline (the determinism proof: its run key is the baseline's).</summary>
    public SimulationRequest BaselineRequest() => Request(Baseline.Fills, BaselineExits);

    /// <summary>The request of the candidate: the baseline's with the one knob changed.</summary>
    public SimulationRequest CandidateRequest(CheckedKnob knob)
    {
        ArgumentNullException.ThrowIfNull(knob);
        if (knob.Knob.Kind == ExperimentKnobKind.Exit)
        {
            return Request(Baseline.Fills, new ExitChoice([WithValue(Baseline.Exit!, knob.Knob.Name, knob.Candidate)], Baseline.TimeStop));
        }

        return Request(WithSlippage(Baseline.Fills, knob.Knob.Name, knob.Candidate), BaselineExits);
    }

    private SimulationRequest Request(FillRules fills, ExitChoice? exits)
    {
        var rules = new FillRulesInput(fills.EntryLimitPercent, fills.Slippage.Ticks, fills.Slippage.BarRangeFrac, fills.Slippage.StopExitExtraTicks);
        return new SimulationRequest(
            Baseline.StrategyId,
            Baseline.Parameters,
            Baseline.Timeframe,
            Baseline.From,
            Baseline.To,
            Baseline.BasketId is null ? Baseline.InstrumentIds : null,
            Baseline.BasketId,
            Baseline.Costs,
            Baseline.AcknowledgeCsvAdjusted,
            Baseline.Options,
            rules,
            exits,
            null,
            Stored ? Baseline.DefinitionSha256 : null);
    }

    private static ExitCandidate WithValue(ExitCandidate exit, string knobName, double value)
    {
        var parameter = knobName[(knobName.IndexOf('.', StringComparison.Ordinal) + 1)..];
        return new ExitCandidate(exit.Policy, [.. exit.Parameters.Select(p => p.Name == parameter ? new ExitParameterValue(p.Name, [value]) : p)]);
    }

    private static FillRules WithSlippage(FillRules fills, string name, double value)
    {
        var s = fills.Slippage;
        var ticks = name == "slippage.ticks" ? value : s.Ticks;
        var range = name == "slippage.bar_range_frac" ? value : s.BarRangeFrac;
        var extra = name == "slippage.stop_exit_extra_ticks" ? value : s.StopExitExtraTicks;
        return FillRules.Create(fills.EntryLimitPercent, SlippageConfig.Create(ticks, range, extra).Value).Value;
    }
}
