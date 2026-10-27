namespace XDrishti.Domain.Research;

/// <summary>
/// Why an instrument of a run has no trades (spec P3-04 Scope 8, P3-04a D3, P3-03b), in the order of precedence: the first that applies. Stored by name and written as <see cref="SkipReasons.ToCode"/>; never renamed.
/// A run in which every instrument is skipped succeeds.
/// </summary>
public enum SkipReason
{
    /// <summary>The instrument has no row (decided by the runner).</summary>
    InstrumentNotFound,

    /// <summary>The read was empty (decided by the replay, whatever a preset skip says).</summary>
    NoBars,

    /// <summary>An applied action that changes the bars is already in a CSV-adjusted series and the run was not acknowledged (decided by the runner, preset).</summary>
    CsvAdjustedTwice,

    /// <summary>The tick size in the instrument master is outside (0, 1] rupee (decided by the runner, preset).</summary>
    TickSizeInvalid,

    /// <summary>No session ran, or no session that ran gave a window step.</summary>
    NoRunnableSessions,

    /// <summary>Sessions ran, none dated in the window.</summary>
    NoSessionsInWindow,

    /// <summary>Window steps above 0, but no window step had every item of <c>setup</c> and <c>trigger</c> ready on any side.</summary>
    FeaturesNotReady,

    /// <summary>The strategy reads the market context and there is none to give it: no stored day, or a set the indicator library refuses (decided by the replay, after a preset skip; P3-lift, ADR 0145).</summary>
    ContextUnavailable,
}

/// <summary>The stable spelling of <see cref="SkipReason"/> in the run record and the run key.</summary>
public static class SkipReasons
{
    /// <summary><c>instrument_not_found</c> · <c>no_bars</c> · <c>csv_adjusted_twice</c> · <c>tick_size_invalid</c> · <c>no_runnable_sessions</c> · <c>no_sessions_in_window</c> · <c>features_not_ready</c> · <c>context_unavailable</c>.</summary>
    public static string ToCode(this SkipReason reason) => reason switch
    {
        SkipReason.InstrumentNotFound => "instrument_not_found",
        SkipReason.NoBars => "no_bars",
        SkipReason.CsvAdjustedTwice => "csv_adjusted_twice",
        SkipReason.TickSizeInvalid => "tick_size_invalid",
        SkipReason.NoRunnableSessions => "no_runnable_sessions",
        SkipReason.NoSessionsInWindow => "no_sessions_in_window",
        SkipReason.FeaturesNotReady => "features_not_ready",
        SkipReason.ContextUnavailable => "context_unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown skip reason."),
    };
}
