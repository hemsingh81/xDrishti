using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.SimAll;

/// <summary>
/// The configuration section <c>Simulate</c> (spec AU-12, ADR 0248): the bounds of the simulate-everything job. A value out of range fails start-up. The defaults suit the 10 GiB VM: two chunks at once (each holds one instrument's bars of one window in memory), a
/// 40 GiB free-disk floor above the recorders' floor of 20, no work in market hours, and a chunk that runs 30 minutes is failed so it cannot hold a worker. The job reads stored bars only: it makes no broker call at all.
/// </summary>
public sealed class SimAllSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Simulate";

    /// <summary>Whether this host runs the job loop (off for a host that only serves the API of the tests).</summary>
    public bool RunWorker { get; set; } = true;

    /// <summary>Chunks that run at once (CPU and memory bound).</summary>
    [Range(1, 16)]
    public int Workers { get; set; } = 2;

    /// <summary>The job waits while free disk is under this many GiB.</summary>
    [Range(10, 500)]
    public int MinFreeGb { get; set; } = 40;

    /// <summary>Whether the job also runs from 09:00 to 15:45 IST on a trading day (default off).</summary>
    public bool RunInMarketHours { get; set; }

    /// <summary>A chunk still running after this many minutes is failed with <c>simulate_all.chunk_timeout</c>.</summary>
    [Range(1, 240)]
    public int ChunkTimeoutMinutes { get; set; } = 30;

    /// <summary>How often the loop looks at the job (and at the conditions that pause it).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>One GiB in bytes.</summary>
    public const long Gb = 1024L * 1024 * 1024;

    /// <summary>The disk floor in bytes.</summary>
    public long MinFreeBytes => MinFreeGb * Gb;
}
