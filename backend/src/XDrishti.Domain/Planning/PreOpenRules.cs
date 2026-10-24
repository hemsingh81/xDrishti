using System.Globalization;

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

    public static PreOpenDecision Decide(PreOpenReading reading, double gapInvalidateAtr = DefaultGapInvalidateAtr)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.Last is not > 0 || !double.IsFinite(reading.Last.Value))
        {
            return Invalidate(PreOpenCodes.UnavailableQuote, "No usable pre-open price.");
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
