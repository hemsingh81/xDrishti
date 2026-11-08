using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// The liquidity tier of an instrument on an entry date for a run with conservative slippage (CF-05, D2, ADR 0229): the 20-day median of close x volume over the sessions strictly before the date (what was known at the entry), against the cuts of
/// <see cref="CostStressOptions"/>. Fewer than 20 usable sessions is <see cref="LiquidityTier.Unavailable"/>, which the walker excludes and counts, never prices as tier A. Same rule as the tier block of the Lab verdict.
/// </summary>
internal sealed class EntryTiers(IBarQueries bars, CostStressOptions options) : IEntryTierSource
{
    /// <summary>Calendar days read before the window: 20 sessions fit in about 28 calendar days; the rest is room for holidays and halts.</summary>
    private const int LookBackDays = 60;

    /// <summary>Reads the day bars of the instrument around the window and returns its tier lookup.</summary>
    public async ValueTask<Func<DateOnly, LiquidityTier>> ForAsync(Guid instrumentId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await bars.ReadDailyBarsAsync(instrumentId, from.AddDays(-LookBackDays), to.AddDays(1), cancellationToken);
        return Of([.. rows.Select(r => new DailyBar(IndiaTime.Today(r.Ts), r.Open, r.High, r.Low, r.Close, r.Volume))], options);
    }

    /// <summary>The tier lookup over day bars (oldest first); each date is judged once.</summary>
    public static Func<DateOnly, LiquidityTier> Of(IReadOnlyList<DailyBar> dayBars, CostStressOptions options)
    {
        ArgumentNullException.ThrowIfNull(dayBars);
        ArgumentNullException.ThrowIfNull(options);
        var cache = new Dictionary<DateOnly, LiquidityTier>();
        return date =>
        {
            if (!cache.TryGetValue(date, out var tier))
            {
                var prior = dayBars.Where(b => b.Date < date).Select(LiquidityTiers.TradedValue).ToList();
                tier = LiquidityTiers.Of(LiquidityTiers.Median(prior), options.TierACut, options.TierBCut);
                cache[date] = tier;
            }

            return tier;
        };
    }
}
