namespace XDrishti.Domain.Research;

/// <summary>The texts of the run header that depend on the costs (ADR 0042, D15). They say what was charged and never what it earned.</summary>
public static class CostDescription
{
    /// <summary>The label for a gross run.</summary>
    public const string None = "costs: none";

    private const string TestNote = " (TEST sheet: synthetic rates, not the broker's)";

    private const string UncheckedNote = " (not checked against bills)";

    /// <summary>
    /// <c>costs: none</c> for a gross run; else <c>costs: sheet &lt;id&gt;</c>, followed by a note when the id starts with <c>test-</c> and by another when the sheet has not been checked against bills.
    /// </summary>
    public static string Label(CostRecord? costs)
    {
        if (costs is null)
        {
            return None;
        }

        return "costs: sheet " + costs.SheetId
            + (costs.SheetId.StartsWith(CostSheet.TestPrefix, StringComparison.Ordinal) ? TestNote : string.Empty)
            + (costs.Checked is null ? UncheckedNote : string.Empty);
    }

    /// <summary>The sizing line of a costed run: the capital, the profile and what is skipped and counted.</summary>
    /// <exception cref="ArgumentNullException">The record is null.</exception>
    public static string SizingLabel(CostRecord costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        return "sizing: fixed simulated capital " + CanonicalText.Number(costs.Capital) + ", profile " + costs.Profile.Name
            + " (risk " + CanonicalText.Number(costs.Profile.RiskPerTradePct) + " %, cap " + CanonicalText.Number(costs.Profile.MaxCapitalPerTradePct)
            + " % of capital per trade); a trade under 1 share or whose charge is above " + CanonicalText.Number(costs.MaxCostR) + " of its risk taken is skipped and counted";
    }
}
