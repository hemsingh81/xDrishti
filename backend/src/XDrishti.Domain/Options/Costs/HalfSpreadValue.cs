namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// The answer to "what is the half-spread of this leg on this date" (spec P14-07): a percent of the mid and the label that says which kind it is, or <see cref="Unavailable"/> with the reason. <see cref="FromRecentArchive"/> is set when a measured
/// model is applied to a date before its first session; P14-09 counts it per trade.
/// </summary>
public sealed record HalfSpreadValue(HalfSpreadSource Source, decimal? Pct, string Label, string? Reason, bool FromRecentArchive)
{
    /// <summary>The reason of an unavailable spread.</summary>
    public const string SpreadUnavailable = "spread_unavailable";

    /// <summary>The flag of a measured spread used for a date before its first session.</summary>
    public const string SpreadFromRecentArchive = "spread_from_recent_archive";

    public bool Available => Source != HalfSpreadSource.None && Pct is not null;

    public static HalfSpreadValue Unavailable { get; } = new(HalfSpreadSource.None, null, "spread: unavailable (" + SpreadUnavailable + ")", SpreadUnavailable, false);
}
