using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Iv;

/// <summary>
/// Which rolling series the <c>iv-1</c> features read (P14-11, ADR 0199, pure): NIFTY on the weekly series (codes 1 and 2), BANKNIFTY on the monthly series (codes 1 and 2), every share on the monthly series (code 1 only), the bar widths of <see cref="SeriesPlanner"/>. The front series is the
/// nearest whose expiry is at least <see cref="MinSessionsToExpiry"/> trading sessions after the trade date; the back series is the next code when the archive holds it. The expiry dates are those of <see cref="ExpiryCalendar"/> (stored contracts, else the derived and unconfirmed weekday rule).
/// </summary>
public static class IvSeries
{
    /// <summary>The fewest sessions between the trade date (exclusive) and the expiry (inclusive) for a series to be the front series.</summary>
    public const int MinSessionsToExpiry = 3;

    /// <summary>The kind of series the underlying is read on.</summary>
    public static char FlagOf(string canonicalSymbol)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbol);
        return canonicalSymbol == ExpiryRules.Nifty ? 'W' : 'M';
    }

    /// <summary>How many codes the backfill holds for the underlying (ADR 0177 decision 3): two for the indices, one for a share.</summary>
    public static int HeldCodes(string canonicalSymbol)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbol);
        return canonicalSymbol is ExpiryRules.Nifty or ExpiryRules.BankNifty ? 2 : 1;
    }

    /// <summary>The bar width of the underlying's series.</summary>
    public static int IntervalOf(string canonicalSymbol)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbol);
        return canonicalSymbol is ExpiryRules.Nifty or ExpiryRules.BankNifty ? SeriesPlanner.IndexInterval : SeriesPlanner.ShareInterval;
    }

    /// <summary>The plan of the trade date, or null when no held series has enough sessions to expiry (or no expiry date can be given).</summary>
    public static IvSeriesPlan? Plan(string canonicalSymbol, DateOnly tradeDate, ExpiryCalendar expiries, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbol);
        ArgumentNullException.ThrowIfNull(expiries);
        ArgumentNullException.ThrowIfNull(calendar);
        var flag = FlagOf(canonicalSymbol);
        var held = HeldCodes(canonicalSymbol);
        for (var code = 1; code <= held; code++)
        {
            if (expiries.Nth(flag, tradeDate, code) is not { } expiry)
            {
                return null;
            }

            if (calendar.TradingDaysBetween(tradeDate, expiry.Date) >= MinSessionsToExpiry)
            {
                return new IvSeriesPlan(flag, code, expiry.Date, code < held ? code + 1 : null, IntervalOf(canonicalSymbol));
            }
        }

        return null;
    }
}
