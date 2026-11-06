namespace XDrishti.Domain.Readiness;

/// <summary>Where the forward mean sits against the point-in-time backtest band of P10-04 (spec CF-11 D2, backtest agreement).</summary>
public enum BandPosition
{
    /// <summary>The mean is below the band's 5th percentile.</summary>
    BelowBand = 0,

    /// <summary>The mean is inside the band (or above it: only a mean below the 5th percentile is penalised).</summary>
    InsideBand = 1,
}
