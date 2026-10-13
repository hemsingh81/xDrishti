namespace XDrishti.Domain.Platform.Services;

/// <summary>The stable codes of <see cref="ScheduleIssue"/>.</summary>
public static class ScheduleIssueCodes
{
    // errors
    public const string TypeInvalid = "type_invalid";
    public const string TypeNotAllowed = "type_not_allowed";
    public const string TimeInvalid = "time_invalid";
    public const string DayInvalid = "day_invalid";
    public const string NoDay = "no_day";
    public const string WindowOrder = "window_order";
    public const string IntervalRange = "interval_range";
    public const string IntervalExceedsWindow = "interval_exceeds_window";
    public const string AfterMissing = "after_missing";
    public const string AfterUnknown = "after_unknown_service";
    public const string ChainCircular = "chain_circular";
    public const string ChainTargetContinuous = "chain_target_continuous";
    public const string RetriesRange = "retries_range";
    public const string TimeoutRange = "timeout_range";
    public const string ParameterUnknown = "parameter_unknown";
    public const string ParameterInvalid = "parameter_invalid";

    /// <summary>The stored schedule of the service cannot be read: it runs on its defaults until a new schedule is saved.</summary>
    public const string StoredInvalid = "schedule.stored_invalid";

    // warnings
    public const string StartTooEarly = "start_too_early";
    public const string StartTooLate = "start_too_late";
    public const string WindowNotCovering = "window_not_covering";
    public const string OverlapsMarketHours = "overlaps_market_hours";
    public const string ChainLong = "chain_long";
    public const string SameMinute = "same_minute";
    public const string MustFollow = "must_follow";
}
