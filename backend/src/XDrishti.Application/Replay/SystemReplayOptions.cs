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

    /// <summary>The disk floor in bytes.</summary>
    public long MinFreeBytes => MinFreeGb * 1024L * 1024 * 1024;
}
