using System.ComponentModel.DataAnnotations;
using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Feed;

/// <summary>
/// The numbers of the depth recorder (<c>Depth:*</c>, P7-16, documented in doc 16), bounds-checked at start. <see cref="Enabled"/> is the deployment gate and is <b>false</b> by default: with it false the recorder never reads credentials, builds a URL or
/// opens a socket (it takes a restart to change). The owner's stored switch and the paused service are the other two gates.
/// </summary>
internal sealed class DepthOptions
{
    public const string SectionName = "Depth";

    /// <summary>The deployment gate (<c>Depth__Enabled</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>The most instruments in a day's set; Dhan allows 50 on one connection.</summary>
    [Range(1, 50)]
    public int MaxInstruments { get; set; } = 50;

    /// <summary>The most of them that come from today's armed tickets.</summary>
    [Range(0, 50)]
    public int MaxArmed { get; set; } = 20;

    /// <summary>Canonical symbols always recorded (after the armed tickets).</summary>
    public List<string> PinnedSymbols { get; set; } = [];

    /// <summary>At most one snapshot per instrument per this many milliseconds.</summary>
    [Range(500, 5000)]
    public int SampleMs { get; set; } = 1000;

    /// <summary>How often the queue is written to the database.</summary>
    [Range(1, 30)]
    public int FlushSeconds { get; set; } = 5;

    /// <summary>The stored size above which recording stops (GiB).</summary>
    [Range(1, 500)]
    public int MaxStoredGb { get; set; } = 40;

    /// <summary>The free disk space below which recording stops (GiB).</summary>
    [Range(5, 500)]
    public int MinFreeGb { get; set; } = 20;

    /// <summary>The settings the recorder works with.</summary>
    public DepthSettings ToSettings() => new(
        Enabled,
        MaxInstruments,
        MaxArmed,
        [.. PinnedSymbols.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())],
        TimeSpan.FromMilliseconds(SampleMs),
        TimeSpan.FromSeconds(FlushSeconds),
        MaxStoredGb * DepthSettings.Gb,
        MinFreeGb * DepthSettings.Gb);
}
