namespace XDrishti.Domain.Platform.Services;

/// <summary>How a service is scheduled (ADR 0018 §2, design doc 04 §3.1). Stored by lower-case name (<see cref="ScheduleTypeCodes"/>).</summary>
public enum ScheduleType
{
    /// <summary>At a time of day, on chosen days or on trading days.</summary>
    Daily,

    /// <summary>On one weekday at a time.</summary>
    Weekly,

    /// <summary>Each time another service finishes successfully.</summary>
    After,

    /// <summary>A service that is up between two times of day.</summary>
    Window,

    /// <summary>Every N minutes within a window of the day.</summary>
    Interval,

    /// <summary>Always up.</summary>
    Continuous,
}
