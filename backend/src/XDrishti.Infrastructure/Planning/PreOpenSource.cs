using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Market;
using XDrishti.Domain.Planning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The pre-open facts of the approved tickets (spec P7-11, ADR 0096): the last price and the previous close from the existing read-only market quote (<see cref="IQuoteSource"/>, the primary Data account; the same endpoint and credential the
/// holdings poll uses, nothing new), and the 14-session average true range from the stored day bars of sessions before today. A symbol the master list cannot map, a quote Dhan did not give, or too few bars leaves that part null (the rules then
/// invalidate). The quote's last trade time travels with the price (the rules refuse a price that is not from today's pre-open session). The range counts only when the newest day bar is the previous trading session. A failed quote request returns no readings
/// (the bars are not read) and a safe problem sentence (never a token, a URL or Dhan's answer). Read only.
/// </summary>
internal sealed class PreOpenSource(IInstrumentSecurities securities, IQuoteSource quotes, IBarQueries bars, ITradingCalendar calendar) : IPreOpenSource
{
    /// <summary>Calendar days of day bars read before today (14 sessions plus holidays and slack).</summary>
    public const int LookbackDays = 45;

    public async ValueTask<PreOpenSnapshot> ReadAsync(IReadOnlyCollection<string> symbols, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var found = await securities.FindAsync(symbols, cancellationToken);
        var result = await quotes.QuoteAsync([.. found.Values.Select(f => f.SecurityId).Distinct(StringComparer.Ordinal)], cancellationToken);
        if (result.Failure is { } failure)
        {
            // No price at all: every ticket will be invalidated, so the bars are not read.
            return new PreOpenSnapshot(new Dictionary<string, PreOpenReading>(StringComparer.Ordinal), failure.Message);
        }

        var quoteById = result.Quotes.GroupBy(q => q.SecurityId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // The range counts only if the newest bar is the previous trading session (a stale series is not a range of the days just gone).
        var previousSession = (await calendar.GetAsync(cancellationToken)).LastClosedSession(new DateTimeOffset(today.ToDateTime(new TimeOnly(9, 10)), IndiaTime.Offset));
        var readings = new Dictionary<string, PreOpenReading>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (!found.TryGetValue(symbol, out var security))
            {
                continue;
            }

            var quote = quoteById.GetValueOrDefault(security.SecurityId);
            var daily = await bars.ReadDailyBarsAsync(security.InstrumentId, today.AddDays(-LookbackDays), today, cancellationToken);
            var complete = daily.Where(b => b.Complete).ToList();
            var current = complete.Count > 0 && IndiaTime.Today(complete[^1].Ts) == previousSession;
            readings[symbol] = new PreOpenReading(
                quote is null ? null : (double)quote.LastPrice,
                quote?.PreviousClose is { } previous ? (double)previous : null,
                current ? AverageTrueRange.Of([.. complete.Select(b => new DailyRange(b.High, b.Low, b.Close))]) : null,
                quote?.LastTradeTime);
        }

        return new PreOpenSnapshot(readings, null);
    }
}
