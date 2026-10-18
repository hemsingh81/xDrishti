using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>The texts of the run header that depend on the fill rules (P3-03b criterion 22).</summary>
public static class FillDescription
{
    /// <summary>The first two header labels: whether the fills are modelled, and the entry limit.</summary>
    public static IReadOnlyList<string> Labels(FillRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return
        [
            rules.Slippage.IsNone ? "fills: exact" : "fills: modelled",
            string.Create(CultureInfo.InvariantCulture, $"entry limit: +{rules.EntryLimitPercent.ToString("R", CultureInfo.InvariantCulture)} %"),
        ];
    }
}
