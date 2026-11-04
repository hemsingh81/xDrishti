using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>The fill rules of a simulation run: the entry limit percent and the slippage. Only <see cref="Create"/> builds one.</summary>
public sealed record FillRules
{
    public const double MaxEntryLimitPercent = 5;

    /// <summary>The most minutes the entry fill may be delayed (CF-05): the fill window itself is five minutes.</summary>
    public const int MaxEntryDelayMinutes = 5;

    private FillRules(double entryLimitPercent, SlippageConfig slippage, int entryDelayMinutes = 0)
    {
        EntryLimitPercent = entryLimitPercent;
        Slippage = slippage;
        EntryDelayMinutes = entryDelayMinutes;
    }

    public double EntryLimitPercent { get; }

    public SlippageConfig Slippage { get; }

    /// <summary>Minutes the entry fill is delayed (CF-05, D3): 0, the default, is the rule of P3-03b; N makes the fill minute the N-th minute that holds a trade after the first one, the rule being otherwise unchanged. Part of the canonical text only when above 0, so a run without it keeps its text and its key.</summary>
    public int EntryDelayMinutes { get; }

    /// <summary>Doc 07 §2 "limit +0.1 % max" and the doc 16 slippage defaults.</summary>
    public static FillRules Default { get; } = new(0.1, SlippageConfig.Default);

    /// <summary><see cref="FillBasis.Exact"/> when the slippage is <see cref="SlippageConfig.None"/>, else <see cref="FillBasis.Modelled"/>: what every trade of a run with these rules says about its fills.</summary>
    public FillBasis Basis => Slippage.IsNone ? FillBasis.Exact : FillBasis.Modelled;

    /// <summary>The stable text stored with a run: invariant culture, fixed key order, no white space, shortest round-trip numbers.</summary>
    public string ToCanonicalJson() =>
        string.Create(CultureInfo.InvariantCulture,
            $"{{\"entry_limit_percent\":{Number(EntryLimitPercent)},\"slippage\":{{\"ticks\":{Number(Slippage.Ticks)},\"bar_range_frac\":{Number(Slippage.BarRangeFrac)},\"stop_exit_extra_ticks\":{Number(Slippage.StopExitExtraTicks)},\"gap_fill\":\"at_open\"}}{(EntryDelayMinutes > 0 ? $",\"entry_delay_minutes\":{EntryDelayMinutes}" : string.Empty)}}}");

    public static Result<FillRules> Create(double entryLimitPercent, SlippageConfig slippage, int entryDelayMinutes = 0)
    {
        if (entryDelayMinutes is < 0 or > MaxEntryDelayMinutes)
        {
            return Result.Failure<FillRules>(SimulationErrors.FillRulesInvalid(nameof(EntryDelayMinutes), $"must be a whole number from 0 to {MaxEntryDelayMinutes.ToString(CultureInfo.InvariantCulture)}"));
        }

        if (!(entryLimitPercent >= 0 && entryLimitPercent <= MaxEntryLimitPercent))
        {
            return Result.Failure<FillRules>(SimulationErrors.FillRulesInvalid(nameof(EntryLimitPercent), $"must be a number from 0 to {MaxEntryLimitPercent.ToString(CultureInfo.InvariantCulture)}"));
        }

        if (slippage is null)
        {
            return Result.Failure<FillRules>(SimulationErrors.FillRulesInvalid(nameof(Slippage), "is required"));
        }

        // -0.0 passes the range check and is a different double: it would print as "-0" in the canonical JSON. Zero is always +0.
        return Result.Success(new FillRules(entryLimitPercent == 0 ? 0 : entryLimitPercent, slippage, entryDelayMinutes));
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
