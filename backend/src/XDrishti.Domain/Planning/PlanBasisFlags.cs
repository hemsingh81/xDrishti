using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Planning;

/// <summary>
/// The smart-rule and data states of a plan as stored facts (spec P8-14, ADR 0113): what the screen used to read out of the free-text basis. Pure: a function of the smart plan, the open-book state and the chosen candidates.
/// </summary>
/// <param name="SchemaVersion">1.</param>
/// <param name="Gate">normal, reduced, no_trade, unavailable or off.</param>
/// <param name="GateMissing">The gate inputs that were not known.</param>
/// <param name="Throttle">clear, cut, unavailable or off.</param>
/// <param name="Correlation">applied, unavailable or off.</param>
/// <param name="CorrelationUnavailablePairs">Pairs with no correlation figure, not checked.</param>
/// <param name="OpenBook">known, assumed_empty or unavailable.</param>
/// <param name="RiskMultiplier">Gate times throttle risk factor.</param>
/// <param name="RuleRemovedHalf">Whether a rule removed half or more of the candidates it saw.</param>
/// <param name="CostBasis">gross, costed_unconfirmed, net_confirmed, mixed or unknown.</param>
/// <param name="Banner">The full text of every flag line, as the engine wrote it.</param>
public sealed record PlanBasisFlags(
    int SchemaVersion,
    string Gate,
    IReadOnlyList<string> GateMissing,
    string Throttle,
    string Correlation,
    int CorrelationUnavailablePairs,
    string OpenBook,
    double RiskMultiplier,
    bool RuleRemovedHalf,
    string CostBasis,
    IReadOnlyList<string> Banner)
{
    /// <summary>The open book is known.</summary>
    public const string OpenBookKnown = "known";

    /// <summary>The owner setting says the open book is empty.</summary>
    public const string OpenBookAssumedEmpty = "assumed_empty";

    /// <summary>The open book is not known.</summary>
    public const string OpenBookUnavailable = "unavailable";

    /// <summary>The flags of a built plan. <paramref name="primaries"/> are the candidates that became primary tickets.</summary>
    public static PlanBasisFlags From(SmartPlan smart, string openBook, IReadOnlyList<RankedCandidate> primaries)
    {
        ArgumentNullException.ThrowIfNull(smart);
        ArgumentNullException.ThrowIfNull(primaries);
        return new PlanBasisFlags(
            1,
            smart.Gate.Status switch
            {
                RuleStatus.Off => "off",
                RuleStatus.Unavailable => "unavailable",
                _ => smart.Gate.Mode switch
                {
                    GateMode.Reduced => "reduced",
                    GateMode.NoTrade => "no_trade",
                    GateMode.Unavailable => "unavailable",
                    _ => "normal",
                },
            },
            [.. smart.Gate.MissingInputs],
            smart.Throttle.Status switch
            {
                RuleStatus.Off => "off",
                RuleStatus.Unavailable => "unavailable",
                _ => smart.Throttle.Code == SmartRuleCodes.ThrottleCut ? "cut" : "clear",
            },
            smart.Correlation switch { RuleStatus.Off => "off", RuleStatus.Unavailable => "unavailable", _ => "applied" },
            smart.CorrelationUnavailablePairs,
            openBook,
            smart.RiskMultiplier,
            smart.Flags.Any(f => f.Code == SmartRuleCodes.RuleRemovedHalf),
            CostBasisOf(primaries),
            smart.Banner);
    }

    // The cell statistics say only whether R is net of charges, never whether the sheet is confirmed as the owner's, so net_confirmed is never written here.
    private static string CostBasisOf(IReadOnlyList<RankedCandidate> primaries)
    {
        var known = primaries.Select(p => p.Merged.Winner.Candidate.Stats?.Costed).Where(c => c is not null).Distinct().ToList();
        return known.Count switch
        {
            0 => "unknown",
            1 => known[0] == true ? "costed_unconfirmed" : "gross",
            _ => "mixed",
        };
    }
}
