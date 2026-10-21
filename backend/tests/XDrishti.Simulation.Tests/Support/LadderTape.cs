using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The closed 15-minute bars of a list of 1-minute bars, in the two forms the P4-01 tests need: the engine's <see cref="ExitTape"/> (features <c>low</c> and <c>high</c>) and the oracle's <see cref="OracleLadderBar"/> list. A bar is the
/// minutes of one India date in one 15-minute bucket of the day counted from 09:15; it closes at the end of its last minute present in the list, which is the instant a strategy running on this list could first see it. The two forms come
/// from the same loop so that the comparison of the engine and the oracle reads one input.
/// </summary>
internal static class LadderTape
{
    private const int BucketMinutes = 15;

    private const int FirstMinute = 555;

    /// <summary>The engine's rule with its tape and the oracle's ladder for one list of minutes.</summary>
    internal sealed record Built(ExitContext Context, OracleLadder Ladder, ExitTape Tape);

    internal static Built Of(IReadOnlyList<Bar> minutes, decimal share, bool askLoose = false)
    {
        var tape = new ExitTape([TestLadderRule.LowFeature, TestLadderRule.HighFeature]);
        var bars = new List<OracleLadderBar>();
        var key = long.MinValue;
        double low = 0, high = 0, close = 0;
        var closeEpoch = 0L;
        var open = false;
        Span<double> row = stackalloc double[2];
        foreach (var minute in minutes)
        {
            var date = SessionClock.DateOf(minute.EpochSeconds).DayNumber;
            var bucket = (date * 1000L) + (Math.Max(0, SessionClock.MinuteOfDay(minute.EpochSeconds) - FirstMinute) / BucketMinutes);
            if (open && bucket != key)
            {
                Close(tape, bars, closeEpoch, close, low, high, row);
                open = false;
            }

            if (!open)
            {
                key = bucket;
                low = minute.Low;
                high = minute.High;
                open = true;
            }

            low = Math.Min(low, minute.Low);
            high = Math.Max(high, minute.High);
            close = minute.Close;
            closeEpoch = minute.EpochSeconds + 60;
        }

        if (open)
        {
            Close(tape, bars, closeEpoch, close, low, high, row);
        }

        return new Built(new ExitContext(new TestLadderRule(share, askLoose), tape), new OracleLadder(bars, share, askLoose), tape);
    }

    private static void Close(ExitTape tape, List<OracleLadderBar> bars, long closeEpoch, double close, double low, double high, Span<double> row)
    {
        row[0] = low;
        row[1] = high;
        tape.Append(closeEpoch, close, row);
        bars.Add(new OracleLadderBar(closeEpoch, close, low, high));
    }
}
