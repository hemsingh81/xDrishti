namespace XDrishti.Domain.Market.Quality;

/// <summary>The limits of the checks (<c>Data:Quality:*</c>): a move needs <see cref="SpikeMultiple"/> times the median 1-minute move (and more than 1 %) to be a spike; more than <see cref="MaxMissingPercent"/> % missing minutes is a warning.</summary>
public sealed record QualityThresholds(double SpikeMultiple, double MaxMissingPercent)
{
    /// <summary>A move of at most this fraction is never a spike, whatever the median is.</summary>
    public const double MinSpikeMove = 0.01;
}
