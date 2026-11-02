namespace XDrishti.Domain.Filings;

/// <summary>What the market knew at an instant (P15-02): the state, the current filings (one per period, newest period first) and how many filings of that instrument and kind have no broadcast time (never shown, never dated).</summary>
public sealed record AsOfResult(string State, IReadOnlyList<StoredFiling> Filings, int UnavailableCount)
{
    /// <summary>At least one current filing.</summary>
    public const string Value = "value";

    /// <summary>No current filing and no filing without a broadcast time.</summary>
    public const string NoneVisible = "none_visible";

    /// <summary>No current filing and at least one filing without a broadcast time: the hole is shown, not hidden.</summary>
    public const string Unavailable = "unavailable";
}
