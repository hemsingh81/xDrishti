using XDrishti.Domain.Market;

namespace XDrishti.Domain.Research;

/// <summary>
/// The net for the unit of the stored tick (spec P3-04b1, E8; ADR 0038 item 6). <see cref="TickSpec.FromMasterPaise"/> takes paise and accepts anything from above 0 up to 1 rupee, so a master table that holds rupees (<c>0.05</c>)
/// would convert to 0.0005 and pass: a silent factor of 100 on every limit and every slipped fill. The finest tick of an NSE share is one paisa, so a share or ETF whose converted tick is positive and below that is a suspect.
/// This is a net, not the guard: the guard is the one-time read of three real instruments.
/// </summary>
public static class TickUnitCheck
{
    /// <summary>The smallest tick, in rupees, that an NSE share or ETF has: one paisa. A converted tick below it is a suspect.</summary>
    public const decimal FinestShareTick = 0.01m;

    /// <summary>
    /// The shares and ETFs of <paramref name="probes"/> whose converted tick is positive and below <see cref="FinestShareTick"/>, each with that converted value (the probe holds the stored one), in the order given. An index, a future or
    /// any other type, a null tick (the flagged fallback) and a tick the range of <see cref="TickSpec"/> refuses (above one rupee, skipped later) are never suspects.
    /// </summary>
    public static IReadOnlyList<(TickProbe Probe, decimal ConvertedRupees)> Suspect(IEnumerable<TickProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);

        var suspects = new List<(TickProbe, decimal)>();
        foreach (var probe in probes)
        {
            if (probe.Type is not (InstrumentType.Share or InstrumentType.Etf) || probe.StoredTick is null)
            {
                continue;
            }

            var tick = TickSpec.FromMasterPaise(probe.StoredTick);
            if (tick.IsSuccess && tick.Value.Size < FinestShareTick)
            {
                suspects.Add((probe, tick.Value.Size));
            }
        }

        return suspects.AsReadOnly();
    }
}
