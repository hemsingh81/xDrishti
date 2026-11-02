namespace XDrishti.Domain.Filings;

/// <summary>Sanity checks over the facts of one span and basis (P15-03). They only flag; a number is never corrected.</summary>
public static class FactSanity
{
    public const string RevenueAboveTotalIncome = "revenue_gt_total_income";
    public const string EpsSignDiffersFromPat = "eps_sign_differs_from_pat";
    public const string BasicEpsBelowDiluted = "eps_basic_below_diluted";

    public static IReadOnlyList<string> Flags(IReadOnlyCollection<ParsedFact> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        decimal? Get(string field) => group.FirstOrDefault(f => f.Field == field && f.IsValue)?.Value;
        var flags = new List<string>();
        var revenue = Get(ResultCodes.Revenue);
        var income = Get(ResultCodes.TotalIncome);
        var pat = Get(ResultCodes.Pat);
        var basic = Get(ResultCodes.EpsBasic);
        var diluted = Get(ResultCodes.EpsDiluted);
        if (revenue is { } r && income is { } i && r > i)
        {
            flags.Add(RevenueAboveTotalIncome);
        }

        if (pat is { } p && basic is { } b && p != 0 && b != 0 && (p > 0) != (b > 0))
        {
            flags.Add(EpsSignDiffersFromPat);
        }

        if (pat is > 0 && basic is { } bb && diluted is { } d && Math.Abs(bb) < Math.Abs(d))
        {
            flags.Add(BasicEpsBelowDiluted);
        }

        return flags;
    }
}
