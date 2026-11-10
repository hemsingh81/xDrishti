namespace XDrishti.Domain.Baskets.Derived;

/// <summary>The named reasons an instrument is left out (ADR 0102: every exclusion is named, never silent).</summary>
public static class ExclusionReason
{
    public const string InsufficientHistory = "insufficient_history";
    public const string CorporateActionUnresolved = "corporate_action_unresolved";
    public const string BadData = "bad_data";
    public const string NotInUniverseFilter = "not_in_universe_filter";
    public const string SmallCluster = "small_cluster";
}
