namespace XDrishti.Domain.Planning;

/// <summary>The stable reason codes of the pre-open check (spec P7-11, ADR 0096). Stored in state rows and suggestions; never rename one.</summary>
public static class PreOpenCodes
{
    /// <summary>The gap to the previous close is within the limit: the ticket is armed.</summary>
    public const string GapOk = "gap_ok";

    /// <summary>The gap is larger than the limit (a share of the average true range): invalidated.</summary>
    public const string GapTooLarge = "gap_too_large";

    /// <summary>No usable pre-open price: invalidated (never armed on a missing input).</summary>
    public const string UnavailableQuote = "unavailable_quote";

    /// <summary>The batch of prices looks stale (almost all equal the previous close: probably no pre-open price yet): invalidated.</summary>
    public const string StaleQuote = "stale_quote";

    /// <summary>No previous close: invalidated.</summary>
    public const string UnavailablePreviousClose = "unavailable_previous_close";

    /// <summary>Too few closed sessions for the average true range: invalidated.</summary>
    public const string UnavailableAtr = "unavailable_atr";

    /// <summary>More tickets than the arming cap: the lowest-ranked ones are refused, the others are untouched.</summary>
    public const string CapExceeded = "armed_cap_exceeded";
}
