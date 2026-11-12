using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Replay;

/// <summary>
/// The configuration section <c>Replay</c> (SR-01, AU-08 Internal): the bounds of the system replay and the yield rules of its job (the same rules as the simulate-everything job, ADR 0248). A value out of range fails start-up.
/// </summary>
public sealed class SystemReplayOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Replay";

    /// <summary>
    /// The share of the universe whose bars of a session may be incomplete before the session is <c>data_missing</c> (no new entries; open positions still exit). More than this share is data missing; exactly this share is not. Default 0.2.
    /// </summary>
    [Range(0.0, 1.0)]
    public double IncompleteDataShare { get; set; } = 0.2;

    /// <summary>Whether this host runs the replay job loop (off for a host that only serves the API of the tests).</summary>
    public bool RunWorker { get; set; } = true;

    /// <summary>The job waits while free disk is under this many GiB.</summary>
    [Range(10, 500)]
    public int MinFreeGb { get; set; } = 40;

    /// <summary>Whether the job also runs from 09:00 to 15:45 IST on a trading day (default off).</summary>
    public bool RunInMarketHours { get; set; }

    /// <summary>How often the loop looks at the queue (and at the conditions that make the job wait).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Sessions of bars read ahead of the cut in one chunk of the rolling window (the lookback is read besides). Bounds the memory of a run: the window is the lookback plus this.</summary>
    [Range(1, 250)]
    public int ReadAheadSessions { get; set; } = 40;

    /// <summary>
    /// The most memory, in MiB, the bars of one replay may need (instruments x the lookback and read-ahead sessions x 375 bars x 48 bytes). A start above it is refused with <c>replay.invalid</c> naming the largest scope. The owner raises it together with the <c>mem_limit</c> of <c>xd-api</c>:
    /// the .NET heap limit in a 1 GiB container is about 768 MiB, and the whole API shares it (the budget of the same kind as <c>Training:MaxRows</c>).
    /// </summary>
    [Range(64, 4096)]
    public int MaxWindowMb { get; set; } = 300;

    /// <summary>Bytes of bars one instrument holds in the rolling window (the lookback plus the read-ahead, 375 bars a session, 48 bytes a bar): the start check and <c>xd evidence preflight</c> share this one formula.</summary>
    public long BytesPerInstrument => (ClampedBars.MinuteLookbackDays * 5L / 7 + ReadAheadSessions) * 375 * 48;

    /// <summary>The disk floor in bytes.</summary>
    public long MinFreeBytes => MinFreeGb * 1024L * 1024 * 1024;
}
