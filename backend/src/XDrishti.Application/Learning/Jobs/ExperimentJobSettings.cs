using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>
/// The configuration section <c>Experiments</c> (spec P8-18 Part B, decision D4): one job at a time is not a setting, the rest are, and a value out of range fails start-up. The limits may be lowered; they cannot be raised above the ranges.
/// </summary>
public sealed class ExperimentJobSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Experiments";

    /// <summary>Whether this host runs the jobs (off for a host that only serves the API of the tests).</summary>
    public bool RunWorker { get; set; } = true;

    /// <summary>Jobs created in the last 24 hours, failed and cancelled included.</summary>
    [Range(1, 10)]
    public int MaxJobsPerDay { get; set; } = 3;

    /// <summary>A job still running after this many minutes ends failed with <c>experiments.timeout</c>.</summary>
    [Range(1, 240)]
    public int MaxMinutes { get; set; } = 60;

    /// <summary>How often the dispatcher looks for a queued job.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The window of the daily limit.</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromHours(24);
}
