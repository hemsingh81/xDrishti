namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The closed reasons a value of <c>iv-1</c> is NaN (P14-11, ADR 0199), besides <see cref="FeatureReasons.ShortHistory"/> and <see cref="FeatureReasons.Degenerate"/>. Counted per feature in the extraction record; a NaN is never filled, defaulted or taken from another instrument.</summary>
public static class IvReasons
{
    /// <summary>The instrument is not an option underlying of the archive.</summary>
    public const string NoOptions = "no_options";

    /// <summary>The instrument is an option underlying of the archive, but no held series has enough sessions to expiry on the date (or no expiry date can be given).</summary>
    public const string NoEligibleSeries = "no_eligible_series";

    /// <summary>No bar of the series ends at or before the decision instant on its India date.</summary>
    public const string NoBarBeforeT = "no_bar_before_t";

    /// <summary>The bar has no IV (or an IV that is not a positive finite number) on a side or offset the feature needs.</summary>
    public const string IvMissing = "iv_missing";

    /// <summary>The key-session of a bar the feature needs did not pass the archive quality checks (or the bar is flagged suspect).</summary>
    public const string ArchiveUnusable = "archive_unusable";

    /// <summary>The highest and the lowest daily value of the window are equal, so a rank is undefined.</summary>
    public const string FlatHistory = "flat_history";

    /// <summary>The underlying has no second series in the archive (the shares).</summary>
    public const string NoTermSeries = "no_term_series";

    /// <summary>A bar of the seven offsets has no open interest.</summary>
    public const string OiMissing = "oi_missing";

    /// <summary>The call open interest of the seven offsets sums to zero.</summary>
    public const string ZeroCallOi = "zero_call_oi";

    /// <summary>The results calendar answers unavailable (any fail-closed reason of ADR 0182).</summary>
    public const string EventUnavailable = "event_unavailable";

    public static readonly IReadOnlyList<string> All = [NoOptions, NoEligibleSeries, NoBarBeforeT, IvMissing, ArchiveUnusable, FeatureReasons.ShortHistory, FlatHistory, NoTermSeries, FeatureReasons.Degenerate, OiMissing, ZeroCallOi, EventUnavailable];
}
