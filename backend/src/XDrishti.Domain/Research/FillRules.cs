using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>The fill rules of a simulation run: the entry limit percent and the slippage. Only <see cref="Create"/> builds one.</summary>
public sealed record FillRules
{
    public const double MaxEntryLimitPercent = 5;

    private FillRules(double entryLimitPercent, SlippageConfig slippage)
    {
        EntryLimitPercent = entryLimitPercent;
        Slippage = slippage;
    }

    public double EntryLimitPercent { get; }

    public SlippageConfig Slippage { get; }

    /// <summary>Doc 07 §2 "limit +0.1 % max" and the doc 16 slippage defaults.</summary>
    public static FillRules Default { get; } = new(0.1, SlippageConfig.Default);

    /// <summary><see cref="FillBasis.Exact"/> when the slippage is <see cref="SlippageConfig.None"/>, else <see cref="FillBasis.Modelled"/>: what every trade of a run with these rules says about its fills.</summary>
    public FillBasis Basis => Slippage.IsNone ? FillBasis.Exact : FillBasis.Modelled;

    /// <summary>The stable text stored with a run: invariant culture, fixed key order, no white space, shortest round-trip numbers.</summary>
    public string ToCanonicalJson() =>
        string.Create(CultureInfo.InvariantCulture,
            $"{{\"entry_limit_percent\":{Number(EntryLimitPercent)},\"slippage\":{{\"ticks\":{Number(Slippage.Ticks)},\"bar_range_frac\":{Number(Slippage.BarRangeFrac)},\"stop_exit_extra_ticks\":{Number(Slippage.StopExitExtraTicks)},\"gap_fill\":\"at_open\"}}}}");

    public static Result<FillRules> Create(double entryLimitPercent, SlippageConfig slippage)
    {
        if (!(entryLimitPercent >= 0 && entryLimitPercent <= MaxEntryLimitPercent))
        {
            return Result.Failure<FillRules>(SimulationErrors.FillRulesInvalid(nameof(EntryLimitPercent), $"must be a number from 0 to {MaxEntryLimitPercent.ToString(CultureInfo.InvariantCulture)}"));
        }

        if (slippage is null)
        {
            return Result.Failure<FillRules>(SimulationErrors.FillRulesInvalid(nameof(Slippage), "is required"));
        }

        // -0.0 passes the range check and is a different double: it would print as "-0" in the canonical JSON. Zero is always +0.
        return Result.Success(new FillRules(entryLimitPercent == 0 ? 0 : entryLimitPercent, slippage));
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
