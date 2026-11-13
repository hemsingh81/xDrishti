namespace XDrishti.Application.OptionChain;

/// <summary>The recorder's own state words (the rest are <c>RecorderStates</c>): at most 32 characters.</summary>
public static class ChainStates
{
    /// <summary>Dhan's rate limit was hit in three slots in a row: requests are paused for 15 minutes.</summary>
    public const string RatePaused = "rate_paused";

    /// <summary>All gates are open and the session is asking.</summary>
    public const string Recording = "recording";

    /// <summary>The day's slots are all done.</summary>
    public const string DayDone = "day_done";
}
