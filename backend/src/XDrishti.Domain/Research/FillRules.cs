using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>The fill rules of a simulation run: the entry limit percent and the slippage. Only <see cref="Create"/> builds one.</summary>
public sealed record FillRules
{
    public const double MaxEntryLimitPercent = 5;

    /// <summary>The most minutes the entry fill may be delayed (CF-05): the fill window itself is five minutes.</summary>
    public const int MaxEntryDelayMinutes = 5;

    private FillRules(double entryLimitPercent, SlippageConfig slippage, int entryDelayMinutes = 0, bool conservativeSlippage = false, decimal tierABps = TieredSlippage.TierABps, decimal tierBBps = TieredSlippage.TierBBps, decimal? minTurnoverCrore = null)
    {
        ConservativeSlippage = conservativeSlippage;
        TierABps = tierABps;
        TierBBps = tierBBps;
        MinTurnoverCrore = minTurnoverCrore;
        EntryLimitPercent = entryLimitPercent;
        Slippage = slippage;
        EntryDelayMinutes = entryDelayMinutes;
    }

    public double EntryLimitPercent { get; }

    public SlippageConfig Slippage { get; }

    /// <summary>Minutes the entry fill is delayed (CF-05, D3): 0, the default, is the rule of P3-03b; N makes the fill minute the N-th minute that holds a trade after the first one, the rule being otherwise unchanged. Part of the canonical text only when above 0, so a run without it keeps its text and its key.</summary>
    public int EntryDelayMinutes { get; }

    /// <summary>Conservative tiered slippage (CF-05, D2, ADR 0229): market-type fills pay at least the bps of the instrument's liquidity tier on the entry date (<see cref="TieredSlippage"/>), and a tier C or unavailable instrument has no trade. False, the default, is the rule of P3-03b. Part of the canonical text only when true, so a run without it keeps its text and its key.</summary>
    public bool ConservativeSlippage { get; }

    /// <summary>The tier A floor in bps of a conservative run (CF-05, D2; ADR 0230: a setting). Part of the canonical text only when conservative and not the default, so a default conservative run keeps its text and its key.</summary>
    public decimal TierABps { get; }

    /// <summary>The tier B floor in bps of a conservative run; same rule as <see cref="TierABps"/>.</summary>
    public decimal TierBBps { get; }

    /// <summary>The threshold of the filter <c>min_turnover</c> in crore rupees (CF-05, ADR 0230); null when the strategy does not name the filter. Part of the canonical text only when set, so a run without the filter keeps its text and its key, and another threshold is another run.</summary>
    public decimal? MinTurnoverCrore { get; }

    /// <summary>The bps for a tier at these rules; null for tier C and for an unavailable tier. See <see cref="TieredSlippage.BpsOf(LiquidityTier)"/>.</summary>
    public decimal? BpsOf(LiquidityTier tier) => LiquidityTiers.Bps(tier, TierABps, TierBBps);

    /// <summary>Doc 07 §2 "limit +0.1 % max" and the doc 16 slippage defaults.</summary>
    public static FillRules Default { get; } = new(0.1, SlippageConfig.Default);

    /// <summary><see cref="FillBasis.Exact"/> when the slippage is <see cref="SlippageConfig.None"/>, else <see cref="FillBasis.Modelled"/>: what every trade of a run with these rules says about its fills.</summary>
    public FillBasis Basis => Slippage.IsNone ? FillBasis.Exact : FillBasis.Modelled;

    /// <summary>The stable text stored with a run: invariant culture, fixed key order, no white space, shortest round-trip numbers.</summary>
    public string ToCanonicalJson() =>
        string.Create(CultureInfo.InvariantCulture,
            $"{{\"entry_limit_percent\":{Number(EntryLimitPercent)},\"slippage\":{{\"ticks\":{Number(Slippage.Ticks)},\"bar_range_frac\":{Number(Slippage.BarRangeFrac)},\"stop_exit_extra_ticks\":{Number(Slippage.StopExitExtraTicks)},\"gap_fill\":\"at_open\"}}{(EntryDelayMinutes > 0 ? $",\"entry_delay_minutes\":{EntryDelayMinutes}" : string.Empty)}{(ConservativeSlippage ? ",\"slippage_tiers\":\"conservative\"" : string.Empty)}{(ConservativeSlippage && (TierABps != TieredSlippage.TierABps || TierBBps != TieredSlippage.TierBBps) ? $",\"tier_a_bps\":{Plain(TierABps)},\"tier_b_bps\":{Plain(TierBBps)}" : string.Empty)}{(MinTurnoverCrore is { } crore ? $",\"min_turnover_crore\":{Plain(crore)}" : string.Empty)}}}");

    public static Result<FillRules> Create(double entryLimitPercent, SlippageConfig slippage, int entryDelayMinutes = 0, bool conservativeSlippage = false, decimal tierABps = TieredSlippage.TierABps, decimal tierBBps = TieredSlippage.TierBBps, decimal? minTurnoverCrore = null)
    {
        if (tierABps < 0 || tierBBps < 0 || (minTurnoverCrore is { } m && m <= 0))
        {
            return Result.Failure<FillRules>(SimulationErrors.FillRulesInvalid(nameof(TierABps), "the tier bps and the min turnover must be positive"));
        }

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
        return Result.Success(new FillRules(entryLimitPercent == 0 ? 0 : entryLimitPercent, slippage, entryDelayMinutes, conservativeSlippage, tierABps, tierBBps, minTurnoverCrore));
    }

    private static string Plain(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
