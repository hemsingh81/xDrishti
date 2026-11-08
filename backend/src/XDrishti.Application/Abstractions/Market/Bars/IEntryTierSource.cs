using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>
/// The liquidity tier of an instrument on an entry date, for a run with conservative slippage (CF-05, D2, ADR 0229). One read of the instrument's day bars per call; the lookup it returns is pure and answers
/// <see cref="LiquidityTier.Unavailable"/> when the history is too short, which the walker excludes and never prices as tier A.
/// </summary>
public interface IEntryTierSource
{
    /// <summary>The tier lookup of one instrument for a run window (India dates, inclusive).</summary>
    ValueTask<Func<DateOnly, LiquidityTier>> ForAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The 20-day median traded value (close x volume, rupees) before each entry date, for the filter <c>min_turnover</c> (CF-05, ADR 0230); null for a date when it cannot be known (missing bars, under 20 sessions, missing volume).</summary>
    ValueTask<Func<DateOnly, decimal?>> TurnoverForAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
