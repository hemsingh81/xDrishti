using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Tests.Shared;

/// <summary>
/// Gross trades made by hand for the tests of costing (P3-03c): the ticket of a worked example written as its fields. The R columns are worked out from the prices with the walker's formula so that a hand-made trade is as consistent as a
/// walked one. The instrument and the symbol are invented. Nothing is read from <c>data/</c>.
/// </summary>
internal static class CostTrades
{
    public static readonly Guid Instrument = new("00000000-0000-0000-0000-0000000000c1");

    /// <summary>
    /// A gross trade. <paramref name="entry"/> and <paramref name="exit"/> are the exact prices, <paramref name="entryFilled"/> and <paramref name="exitFilled"/> the filled ones (default: the exact prices);
    /// the entry is dated <paramref name="date"/> and the exit <paramref name="exitDate"/> (default: the same day).
    /// </summary>
    public static SimTrade Make(
        Side side,
        DateOnly date,
        double entry,
        double stop,
        double exit,
        double? entryFilled = null,
        double? exitFilled = null,
        DateOnly? exitDate = null,
        ExitReason reason = ExitReason.Target,
        int entryMinute = 600,
        int exitMinute = 640,
        string strategy = "ORB")
    {
        var entryEpoch = Day.Epoch(date, entryMinute);
        var exitEpoch = Day.Epoch(exitDate ?? date, exitMinute);
        var sign = side == Side.Buy ? 1d : -1d;
        var risk = Math.Abs(entry - stop);
        var filledIn = entryFilled ?? entry;
        var filledOut = exitFilled ?? exit;
        var rExact = sign * (exit - entry) / risk;
        var rFilled = sign * (filledOut - filledIn) / risk;
        return new SimTrade(
            SimTrade.CurrentSchemaVersion, strategy, Instrument, "CST", side, date, entryEpoch - 60, Timeframe.M15, new Bar(entryEpoch - 960, entry, entry, entry, entry, 15000), 15, true,
            entryEpoch, entry, stop, null, exitEpoch, exit, reason, Zero(rExact), 0.4, 2.1, FillBasis.Modelled, CostBasis.None, null,
            Zero(rFilled), Zero(rFilled - rExact), filledIn, filledOut, EntryFill.AtOpen, false, exitDate is { } d && d != date ? 2 : 1, 0);
    }

    /// <summary>W1 (and H1 of P3-03b): BUY, entry 100.00 filled 100.10, stop 99.00, target and exit 102.00.</summary>
    public static SimTrade W1(DateOnly? date = null) => Make(Side.Buy, date ?? new DateOnly(2026, 3, 2), 100.00, 99.00, 102.00, entryFilled: 100.10);

    private static double Zero(double value) => value == 0 ? 0 : value;
}
