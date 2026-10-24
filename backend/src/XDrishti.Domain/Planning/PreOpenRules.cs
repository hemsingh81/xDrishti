using System.Globalization;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Planning;

/// <summary>
/// The pure pre-open check of one approved ticket (spec P7-11, doc 10 section 7): the gap between the pre-open price and the previous close, in rupees, against <c>gap_invalidate_atr</c> times the average true range. Either direction
/// counts (a gap against the ticket is as unfit as one away from it). A missing price, previous close or range invalidates; nothing is armed on a missing input. There is no promotion from the reserve here or anywhere.
/// The ban list, surveillance and the price band have no data source (P15): they are not re-checked here; the approval already carried them as unavailable.
/// </summary>
public static class PreOpenRules
{
    /// <summary>The default share of the average true range (doc 16 <c>gap_invalidate_atr</c>).</summary>
    public const double DefaultGapInvalidateAtr = 0.5;

    /// <summary>The most tickets armed in one session; the rest are refused with <see cref="PreOpenCodes.CapExceeded"/>.</summary>
    public const int MaxArmedTickets = 50;

    /// <summary>The first moment (India time) a price counts as a pre-open price: the pre-open session starts at 09:00.</summary>
    public static readonly TimeSpan PreOpenStart = new(9, 0, 0);

    /// <summary>The first moment a price no longer counts as a pre-open price: the market opens at 09:15.</summary>
    public static readonly TimeSpan MarketOpen = new(9, 15, 0);

    /// <summary>Share (in tenths) of the batch whose price equals the previous close that makes the whole batch look stale.</summary>
    private const int StaleShareTenths = 8;

    /// <summary>The fewest readings for the batch guard to apply (with fewer, one unchanged share is ordinary).</summary>
    private const int StaleMinimum = 3;

    /// <summary>
    /// Whether most of the batch has a price exactly equal to the previous close (at least 80 % of three or more readings): before the open that is what a quote that carries yesterday's close looks like. Only readings that have both numbers count.
    /// </summary>
    public static bool StaleBatch(IReadOnlyCollection<PreOpenReading> readings, out int equal, out int total)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var counted = readings.Where(r => r.Last is > 0 && r.PreviousClose is > 0).ToList();
        total = counted.Count;
        equal = counted.Count(r => r.Last == r.PreviousClose);
        return total >= StaleMinimum && equal * 10 >= total * StaleShareTenths;
    }

    /// <summary>
    /// Decides on one reading. The price must carry a trade time that is today (India date) and within the pre-open session (09:00 up to 09:15); a missing, old or later time reads as no price (fail closed).
    /// </summary>
    public static PreOpenDecision Decide(PreOpenReading reading, DateOnly today, double gapInvalidateAtr = DefaultGapInvalidateAtr)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.Last is not > 0 || !double.IsFinite(reading.Last.Value))
        {
            return Invalidate(PreOpenCodes.UnavailableQuote, "No usable pre-open price.");
        }

        if (reading.QuoteTime is not { } at)
        {
            return Invalidate(PreOpenCodes.UnavailableQuote, "The price carries no trade time, so it cannot be told from yesterday's.");
        }

        var ist = IndiaTime.ToIst(at);
        if (DateOnly.FromDateTime(ist.DateTime) != today || ist.TimeOfDay < PreOpenStart || ist.TimeOfDay >= MarketOpen)
        {
            return Invalidate(PreOpenCodes.UnavailableQuote, string.Create(CultureInfo.InvariantCulture, $"The price is from {ist:yyyy-MM-dd HH:mm:ss} India time, not from today's pre-open session."));
        }

        if (reading.PreviousClose is not > 0 || !double.IsFinite(reading.PreviousClose.Value))
        {
            return Invalidate(PreOpenCodes.UnavailablePreviousClose, "No previous close.");
        }

        if (reading.Atr is not > 0 || !double.IsFinite(reading.Atr.Value))
        {
            return Invalidate(PreOpenCodes.UnavailableAtr, "Too few closed sessions for the average true range.");
        }

        var gap = reading.Last.Value - reading.PreviousClose.Value;
        var limit = gapInvalidateAtr * reading.Atr.Value;
        var text = string.Create(CultureInfo.InvariantCulture, $"gap {gap:+0.00;-0.00;0.00} on a close of {reading.PreviousClose.Value:0.00}, limit {limit:0.00} ({gapInvalidateAtr:0.##} x ATR {reading.Atr.Value:0.00})");
        return Math.Abs(gap) > limit
            ? Invalidate(PreOpenCodes.GapTooLarge, text)
            : new PreOpenDecision(true, PreOpenCodes.GapOk, text);
    }

    private static PreOpenDecision Invalidate(string code, string detail) => new(false, code, detail);
}
