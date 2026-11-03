namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The extension <c>iv-1</c> (P14-11, ADR 0199): the option-implied features, in the order they are stored. A feature set evaluated by the P13-05 comparison; no decision path reads it.</summary>
public static class IvFeatureCatalog
{
    public const string Extension = "iv";

    public const string Version = "iv-1";

    /// <summary>The family of its evaluation records.</summary>
    public const string Family = "ext:iv-1";

    public const string IvAtm = "iv_atm";

    public const string IvRank252 = "iv_rank_252";

    public const string IvPct252 = "iv_pct_252";

    public const string IvTermSlope = "iv_term_slope";

    public const string IvMinusRv20 = "iv_minus_rv20";

    public const string NiftyIvRank252 = "nifty_iv_rank_252";

    public const string PcrOi = "pcr_oi";

    public const string ResultsBeforeExpiry = "results_before_expiry";

    public static readonly IReadOnlyList<string> Names = [IvAtm, IvRank252, IvPct252, IvTermSlope, IvMinusRv20, NiftyIvRank252, PcrOi, ResultsBeforeExpiry];

    public static ExtensionGroup Group { get; } = new(Extension, Version, Names);
}
