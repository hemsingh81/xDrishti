namespace XDrishti.Domain.Research;

/// <summary>How a placebo shape of CF-07 (D2) changes a real signal.</summary>
public enum PlaceboKind
{
    /// <summary>The opposite side with the stop and target mirrored (same risk fraction and target multiple).</summary>
    Mirror = 0,

    /// <summary>The same signal, entered that many minutes later in the same session (earlier is look-ahead and is not offered).</summary>
    ShiftMinutes = 1,

    /// <summary>The same clock-time signal on another session of the same instrument, that many sessions away.</summary>
    ShiftSessions = 2,

    /// <summary>The day's signals reassigned to the instrument that many places on in a fixed order.</summary>
    RollInstrument = 3,
}
