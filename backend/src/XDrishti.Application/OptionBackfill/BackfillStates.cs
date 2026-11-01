namespace XDrishti.Application.OptionBackfill;

/// <summary>The backfill's own state words (the rest are <c>DepthStates</c>): at most 32 characters.</summary>
public static class BackfillStates
{
    /// <summary>All gates are open and the night's fetches are running.</summary>
    public const string Backfilling = "backfilling";

    /// <summary>Every planned key is finished or given up.</summary>
    public const string Done = "backfill_done";

    /// <summary>The deployment flag, the switch and the service are on but the owner has not accepted the probe result.</summary>
    public const string ProbeNotAccepted = "probe_not_accepted";

    /// <summary>It is outside the night window (16:30 to 08:30 India time).</summary>
    public const string OutsideWindow = "outside_window";

    /// <summary>The day's request cap was reached; it goes on the next India day.</summary>
    public const string DailyCap = "daily_cap";

    /// <summary>Five answers in a row were rate limited, across keys: the run ended and waits 15 minutes.</summary>
    public const string RatePaused = "rate_paused";

    /// <summary>Three answers in a row could not be stored: the run ended and waits 5 minutes.</summary>
    public const string StoreFailed = "store_failed";

    /// <summary>Five keys in a row were given up: the run ended for the day.</summary>
    public const string TooManyFailures = "too_many_failures";

    /// <summary>Dhan said the request itself is wrong (DH-905): the run ended for the day.</summary>
    public const string BadRequest = "bad_request";
}
