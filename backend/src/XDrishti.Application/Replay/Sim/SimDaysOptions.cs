using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Replay.Sim;

/// <summary>The configuration section <c>Sim</c> (SR-04, ADR 0293): the money and playback defaults of the day-by-day simulation and its runner. A value out of range fails start-up.</summary>
public sealed class SimDaysOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Sim";

    /// <summary>The starting capital in rupees when the start names none (default Rs 10,00,000).</summary>
    [Range(1, 1_000_000_000)]
    public decimal DefaultCapital { get; set; } = 1_000_000m;

    /// <summary>Seconds of playback per minute of market at 1X (default 10).</summary>
    [Range(1, 60)]
    public int DefaultSpeedSecondsPerMinute { get; set; } = 10;

    /// <summary>Off (default): a complete run is extended only by the owner's Continue. On: a newer stored session extends it by itself (no risk widened, no evidence lost).</summary>
    public bool AutoContinue { get; set; }

    /// <summary>Whether this host runs the simulation loop (off for a host that only serves the API of the tests).</summary>
    public bool RunWorker { get; set; } = true;

    /// <summary>How often the loop looks at the active run.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
