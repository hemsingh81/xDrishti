namespace XDrishti.Domain.Filings;

/// <summary>The closed vocabularies of the results facts (P15-03, ADR 0184): field codes, units, spans, bases, parse outcomes and the reasons a fact or a file is unavailable. Stored as text; never extended without an ADR.</summary>
public static class ResultCodes
{
    public const string Revenue = "revenue";
    public const string TotalIncome = "total_income";
    public const string Pbt = "pbt";
    public const string Pat = "pat";
    public const string EpsBasic = "eps_basic";
    public const string EpsDiluted = "eps_diluted";
    public const string ExceptionalItems = "exceptional_items";
    public const string Audited = "audited";

    public const string Inr = "inr";
    public const string InrPerShare = "inr_per_share";
    public const string Flag = "flag";

    public const string Quarter = "q";
    public const string YearToDate = "ytd";
    public const string FullYear = "fy";

    public const string Consolidated = "consolidated";
    public const string Standalone = "standalone";

    public const string Parsed = "parsed";
    public const string Rejected = "rejected";
    public const string NotXbrl = "not_xbrl";
    public const string NoFacts = "no_facts";

    public const string NotInFile = "not_in_file";
    public const string ConflictingFacts = "conflicting_facts";
    public const string UnitUnknown = "unit_unknown";
    public const string ScaleUnknown = "scale_unknown";
    public const string BadValue = "bad_value";
    public const string BasisUnknown = "basis_unknown";
    public const string FormatNotSupported = "format_not_supported";
    public const string NothingMapped = "nothing_mapped";
    public const string NoPeriod = "no_period";

    public const string TooLarge = "too_large";
    public const string Empty = "empty";
    public const string InvalidEncoding = "invalid_encoding";
    public const string NotXml = "not_xml";
    public const string DtdProhibited = "dtd_prohibited";
    public const string ExternalReference = "external_reference";
    public const string TooDeep = "too_deep";
    public const string TooManyElements = "too_many_elements";

    /// <summary>The fields in their fixed order (also the order of every answer).</summary>
    public static readonly IReadOnlyList<string> Fields = [Revenue, TotalIncome, Pbt, Pat, EpsBasic, EpsDiluted, ExceptionalItems, Audited];

    public static readonly IReadOnlyList<string> Spans = [Quarter, YearToDate, FullYear];

    public static readonly IReadOnlyList<string> Bases = [Consolidated, Standalone];

    /// <summary>The unit a field is stored in (the expected unit also marks an unavailable fact).</summary>
    public static string UnitOf(string field) => field switch
    {
        EpsBasic or EpsDiluted => InrPerShare,
        Audited => Flag,
        _ => Inr,
    };
}
