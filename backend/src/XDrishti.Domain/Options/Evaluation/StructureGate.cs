using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>
/// The two gates of the credit structures (P14-10 criterion 3), checked before a trial is counted and before any bar is read: a stored regime (P6-02; without it <c>regime_unavailable</c>) and a measured half-spread of at least the usable-session minimum (P14-07; an owner assumption or a
/// model built from fewer sessions is <c>spread_not_measured</c>: four or eight half-spreads decide a credit structure's result). A failed gate is a NOT_EVALUABLE record naming what is missing, never a verdict on data. Pure.
/// </summary>
public static class StructureGate
{
    public const string RegimeUnavailable = "regime_unavailable";
    public const string SpreadNotMeasured = "spread_not_measured";

    /// <summary>The missing quantities, regime first; empty when both gates pass. <paramref name="measuredSessions"/> is the largest session count of a stored measured model (null: none).</summary>
    public static IReadOnlyList<string> Missing(bool regimeStored, int? measuredSessions, int minSessions)
    {
        List<string> missing = [];
        if (!regimeStored)
        {
            missing.Add(RegimeUnavailable + " (no regime label is stored: P6-02 and P3-01d are not built)");
        }

        if (measuredSessions is not { } sessions || sessions < minSessions)
        {
            missing.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{SpreadNotMeasured} (best measured model {measuredSessions ?? 0} of {minSessions} usable chain sessions; an owner assumption is not enough for a credit structure)"));
        }

        return missing;
    }

    /// <summary>The NOT_EVALUABLE outcome of a failed gate: no estimate, no trial, the missing quantities named.</summary>
    public static ProtocolOutcome NotEvaluable(string protocol, IReadOnlyList<string> missing) =>
        EvidenceBlock.NotEvaluable(protocol, missing, ["credit spreads and condors are evaluated only in a stored range regime and on a measured half-spread", "Advisory research only: no order path exists for a multi-leg structure."]);
}
