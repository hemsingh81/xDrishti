using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>
/// What a proposal from a liquidity-tier observation says (BK-11b, ADR 0266): one knob (<c>scan_scope</c>, kind <see cref="ExperimentKnobKind.Scope"/>), the baseline <c>*</c> (all shares) and the tier as the candidate, with the sentence built from the stored numbers. Nothing is applied: the
/// hypothesis only records that the owner may want to look at the tier. Pure.
/// </summary>
public static class UniverseLinkProposal
{
    /// <summary>The name of the knob.</summary>
    public const string KnobName = "scan_scope";

    /// <summary>The one knob of a tier proposal: from all shares to the tier.</summary>
    public static ExperimentKnob Knob(string tier) => new(KnobName, ExperimentKnobKind.Scope, CellId.AllUniverses, tier);

    /// <summary>The sentence from the stored numbers: <c>ORB, 15m: stronger on liq_high (lower bound 0.53 vs 0.44, n 30/200; hold-out stable)</c>.</summary>
    public static string Rationale(string strategy, string timeframe, string tier, double childLower, double? parentLower, int childN, int parentN, string holdOut) =>
        string.Create(CultureInfo.InvariantCulture, $"{strategy}, {timeframe}: stronger on {tier} (lower bound {childLower:0.00} vs {(parentLower is { } p ? p.ToString("0.00", CultureInfo.InvariantCulture) : "-")}, n {childN}/{parentN}; hold-out {holdOut})");

    /// <summary>Why a link is not an observation, in words; empty text is never returned for a non-observation.</summary>
    /// <param name="reason">The choice reason of the link (<c>child_better</c>, <c>no_better_than_parent</c> or <c>child_thin</c>).</param>
    /// <param name="childN">Trades of the tier.</param>
    /// <param name="minTrades">The link minimum of trades.</param>
    /// <param name="holdOut">The hold-out verdict (<c>stable</c>, <c>unstable</c>, <c>insufficient</c> or <c>not_evaluated</c>).</param>
    public static string NotObservationReason(string reason, int childN, int minTrades, string holdOut) =>
        reason switch
        {
            "child_thin" => string.Create(CultureInfo.InvariantCulture, $"thin: the tier has {childN} trades, too few to be read in place of all shares"),
            "no_better_than_parent" => "not read: the tier is no better than all shares",
            _ when childN < minTrades => string.Create(CultureInfo.InvariantCulture, $"thin: {childN} trades, the minimum of an observation is {minTrades}"),
            _ => holdOut switch
            {
                "insufficient" => "hold-out insufficient: a half of the tier's trades is too small to judge",
                "not_evaluated" => "hold-out not evaluated: compute the cells again with the universe split",
                _ => "hold-out " + holdOut + ": the tier did not beat the rest of all shares in both halves",
            },
        };
}
