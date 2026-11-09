using System.ComponentModel.DataAnnotations;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>Settings of the recovery of missed days (configuration section <c>Recovery</c>, ADR 0240). Not a visible setting: a validated default; an invalid value stops the start-up.</summary>
public sealed class RecoverySettings
{
    public const string SectionName = "Recovery";

    /// <summary>How many of the latest trading sessions are made up for; older ones are reported out of reach.</summary>
    [Range(RecoveryPlanner.MinSessions, RecoveryPlanner.MaxSessionsLimit)]
    public int MaxSessions { get; set; } = RecoveryPlanner.DefaultMaxSessions;

    public const int DefaultDebounceMinutes = 10;

    public const int DefaultMaxAttempts = 3;

    public const int DefaultWaitingNoticeDays = 1;

    /// <summary>
    /// Triggers closer than this to the previous accepted one start no pass. Best practice for wake and reconnect events: they arrive in bursts (a laptop wakes, the network flaps, the broker reconnects within seconds), and one
    /// pass already looks at everything missed, so a short window collapses a burst while a pass later in the day still runs; 10 minutes is long enough for the burst and short enough to react to a real second event.
    /// </summary>
    [Range(1, 120)]
    public int DebounceMinutes { get; set; } = DefaultDebounceMinutes;

    /// <summary>How many times a failed step is tried in all (each attempt is itself a normal run with its own retries); after that it stays failed and raises one notice per service per day.</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>A step that still waits (no connected account, a source that is down) for a session this many India days old or older raises one notice per service per day; the wait itself costs no attempt.</summary>
    [Range(1, 30)]
    public int WaitingNoticeDays { get; set; } = DefaultWaitingNoticeDays;
}
