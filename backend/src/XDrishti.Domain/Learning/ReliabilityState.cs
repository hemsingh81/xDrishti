namespace XDrishti.Domain.Learning;

/// <summary>How the reliability slope reads (spec CF-12, D6).</summary>
public enum ReliabilityState
{
    /// <summary>The slope could not be fitted (no outcomes, one label, all predictions equal, no convergence). The unset state.</summary>
    Unavailable = 0,

    /// <summary>Fewer outcomes than the window: the slope is shown and never alarms.</summary>
    ShownOnly = 1,

    /// <summary>A full window and no alarm.</summary>
    Ok = 2,

    /// <summary>A full window, the slope outside the band and farther than the stated standard errors from 1.</summary>
    Alarm = 3,
}
