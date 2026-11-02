namespace XDrishti.Domain.Filings;

/// <summary>
/// The versioned element-to-field table of the results parser (<c>xbrl-1</c>, P15-03, ADR 0184). Elements are matched by local name. UNVERIFIED: every name below is a starting list taken from the Ind AS element names and is decided by the owner's real filings, not by this text;
/// an element that is not listed is not read, and the field it would have carried is <c>unavailable/not_in_file</c>. The banking format is recognised, never mapped.
/// </summary>
public static class ResultsTaxonomyMap
{
    /// <summary>A mapped element: the field it fills and its rank (a lower rank wins; a higher one is used only when no element of a lower rank is present for the span and basis).</summary>
    public sealed record Mapping(string Field, int Rank);

    public const string BasisMarker = "NatureOfReportStandaloneConsolidated";

    public const string RoundingMarker = "LevelOfRoundingUsedInFinancialStatements";

    public const string BasisAxis = "ConsolidatedAndStandaloneAxis";

    public const string ConsolidatedMember = "ConsolidatedMember";

    public const string StandaloneMember = "StandaloneMember";

    /// <summary>The rank of the "profit for the period" fallback of <c>pat</c>.</summary>
    public const int FallbackRank = 1;

    private static readonly Dictionary<string, Mapping> Elements = new(StringComparer.Ordinal)
    {
        ["RevenueFromOperations"] = new(ResultCodes.Revenue, 0),
        ["Income"] = new(ResultCodes.TotalIncome, 0),
        ["ProfitBeforeTax"] = new(ResultCodes.Pbt, 0),
        ["ProfitOrLossAttributableToOwnersOfParent"] = new(ResultCodes.Pat, 0),
        ["ProfitLossForPeriod"] = new(ResultCodes.Pat, FallbackRank),
        ["BasicEarningsLossPerShareFromContinuingAndDiscontinuedOperations"] = new(ResultCodes.EpsBasic, 0),
        ["DilutedEarningsLossPerShareFromContinuingAndDiscontinuedOperations"] = new(ResultCodes.EpsDiluted, 0),
        ["ExceptionalItemsBeforeTax"] = new(ResultCodes.ExceptionalItems, 0),
        ["WhetherResultsAreAuditedOrUnaudited"] = new(ResultCodes.Audited, 0),
    };

    public static Mapping? Find(string localName) => Elements.GetValueOrDefault(localName);

    /// <summary>The banking taxonomy marker: a namespace that names a bank. UNVERIFIED until a bank filing is supplied.</summary>
    public static bool IsBankingNamespace(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Contains("bank", StringComparison.OrdinalIgnoreCase);
    }
}
