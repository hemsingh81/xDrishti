using System.ComponentModel.DataAnnotations;
using XDrishti.Application.Abstractions.Ticks;

namespace XDrishti.Feed;

/// <summary>
/// The numbers of the trade-tick recorder (<c>Ticks:*</c>, P7-16b, documented in doc 16), bounds-checked at start. <see cref="Enabled"/> is the deployment gate and is <b>false</b> by default: with it false the recorder never reads credentials, builds a
/// URL or opens a socket (it takes a restart to change). The owner's stored switch and the paused service are the other two gates. The set inputs and the free-space floor are the depth recorder's (<c>Depth:*</c>).
/// </summary>
internal sealed class TickOptions
{
    public const string SectionName = "Ticks";

    /// <summary>The deployment gate (<c>Ticks__Enabled</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>How often the queue is written to the database.</summary>
    [Range(1, 30)]
    public int FlushSeconds { get; set; } = 5;

    /// <summary>The stored size of <c>market.trade_ticks</c> above which recording stops (GiB).</summary>
    [Range(1, 500)]
    public int MaxStoredGb { get; set; } = 12;

    /// <summary>The settings the recorder works with; the free floor is the shared <c>Depth:MinFreeGb</c>.</summary>
    public TickSettings ToSettings(int minFreeGb) => new(Enabled, TimeSpan.FromSeconds(FlushSeconds), MaxStoredGb * TickSettings.Gb, minFreeGb * TickSettings.Gb);
}
