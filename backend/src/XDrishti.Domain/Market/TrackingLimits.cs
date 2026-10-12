namespace XDrishti.Domain.Market;

/// <summary>Largest requests the tracking endpoints accept.</summary>
public static class TrackingLimits
{
    /// <summary>Instrument ids in one track or untrack call.</summary>
    public const int MaxIdsPerCall = 500;

    /// <summary>Entries in one resolve call (a pasted list or an uploaded CSV).</summary>
    public const int MaxResolveEntries = 5_000;

    /// <summary>Longest entry of a resolve call that is looked up; longer ones are listed as unmatched (the longest canonical symbol is 100 characters).</summary>
    public const int MaxResolveEntryLength = InstrumentLimits.CanonicalSymbol;
}
