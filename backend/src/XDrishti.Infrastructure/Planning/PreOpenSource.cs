using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The pre-open facts of the approved tickets (spec P7-11, ADR 0096): the last price and the previous close from the existing read-only market quote (<see cref="IQuoteSource"/>, the primary Data account; the same endpoint and credential the
/// holdings poll uses, nothing new), and the 14-session average true range from the stored day bars of sessions before today. A symbol the master list cannot map, a quote Dhan did not give, or too few bars leaves that part null (the rules then
/// invalidate). A failed quote request returns every reading without a price and a safe problem sentence (never a token, a URL or Dhan's answer). Read only.
/// </summary>
internal sealed class PreOpenSource(IInstrumentSecurities securities, IQuoteSource quotes, IBarQueries bars) : IPreOpenSource
{
    /// <summary>Calendar days of day bars read before today (14 sessions plus holidays and slack).</summary>
    public const int LookbackDays = 45;

    public async ValueTask<PreOpenSnapshot> ReadAsync(IReadOnlyCollection<string> symbols, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var found = await securities.FindAsync(symbols, cancellationToken);
        var result = await quotes.QuoteAsync([.. found.Values.Select(f => f.SecurityId).Distinct(StringComparer.Ordinal)], cancellationToken);
        var quoteById = result.Quotes.GroupBy(q => q.SecurityId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var readings = new Dictionary<string, PreOpenReading>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (!found.TryGetValue(symbol, out var security))
            {
                continue;
            }

            var quote = result.Failure is null ? quoteById.GetValueOrDefault(security.SecurityId) : null;
            var daily = await bars.ReadDailyBarsAsync(security.InstrumentId, today.AddDays(-LookbackDays), today, cancellationToken);
            var ranges = daily.Where(b => b.Complete).Select(b => new DailyRange(b.High, b.Low, b.Close)).ToList();
            readings[symbol] = new PreOpenReading(
                quote is null ? null : (double)quote.LastPrice,
                quote?.PreviousClose is { } previous ? (double)previous : null,
                AverageTrueRange.Of(ranges));
        }

        var problem = result.Failure?.Message;
        return new PreOpenSnapshot(readings, problem);
    }
}
