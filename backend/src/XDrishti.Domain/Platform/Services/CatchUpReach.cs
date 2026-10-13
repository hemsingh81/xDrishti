namespace XDrishti.Domain.Platform.Services;

/// <summary>How far back a missed run is still made up for when the application starts (ADR 0018 §4).</summary>
public enum CatchUpReach
{
    /// <summary>Only an occurrence of the same India day (a daily job); a weekly one within 24 hours.</summary>
    SameDay,

    /// <summary>The most recent occurrence however long ago, when no run started since (a list that may simply be refreshed, ADR 0010).</summary>
    Latest,
}
