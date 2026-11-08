using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Research;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>The fill rules a Lab run was posted with (CF-05, ADR 0229): the modelled defaults, or the conservative preset when the run's stored options say so. One place, so the run and the hold-out pass cost the same fills.</summary>
internal static class LabFills
{
    public static FillRulesInput For(LabRun run, CostStressOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        var rules = FillRules.Default;
        var conservative = LabRunOptions.Parse(run.RunOptions).ConservativeSlippage;
        return new FillRulesInput(rules.EntryLimitPercent, rules.Slippage.Ticks, conservative ? (options ?? new CostStressOptions()).RangeFraction : rules.Slippage.BarRangeFrac, rules.Slippage.StopExitExtraTicks, 0, conservative);
    }
}
