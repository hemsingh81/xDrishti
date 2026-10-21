using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// The closed bars of a list of 1-minute bars with one value for a trail, in the two forms the P4-03 tests need: the engine's <see cref="ExitTape"/> with its rule and the oracle's <see cref="OracleTrail"/>. A bar is the minutes of one India
/// date in one bucket of <c>bucketMinutes</c> counted from 09:15 (375 gives the daily bar); it closes at the end of its last minute present in the list. The value is a simple stand-in for the indicator (the walker reads a tape, whatever
/// computed it): the mean range of the last <c>period</c> bars for the ATR, the lowest low (a sell: the highest high) of the last <c>period</c> bars for the structure, an exponential average of the closes with alpha 2/(period+1) for the EMA,
/// each NaN until <c>period</c> bars exist. The two forms come from the same loop so that the comparison of the engine and the oracle reads one input.
/// </summary>
internal static class TrailTape
{
    private const int FirstMinute = 555;

    /// <summary>The engine's rule with its tape, the oracle's trail and the tape.</summary>
    internal sealed record Built(ExitContext Context, OracleTrail Trail, ExitTape Tape);

    internal static Built Of(IReadOnlyList<Bar> minutes, OracleTrailKind kind, Side side, int period, double multiple, double activateAtRr, int bucketMinutes = 15)
    {
        var closes = new List<(long Epoch, double Close, double Low, double High)>();
        var key = long.MinValue;
        double low = 0, high = 0, close = 0;
        var closeEpoch = 0L;
        var open = false;
        foreach (var minute in minutes)
        {
            var date = SessionClock.DateOf(minute.EpochSeconds).DayNumber;
            var bucket = (date * 1000L) + (Math.Max(0, SessionClock.MinuteOfDay(minute.EpochSeconds) - FirstMinute) / bucketMinutes);
            if (open && bucket != key)
            {
                closes.Add((closeEpoch, close, low, high));
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
            closes.Add((closeEpoch, close, low, high));
        }

        var tape = new ExitTape(["value"]);
        var bars = new List<OracleTrailBar>(closes.Count);
        var ema = double.NaN;
        for (var i = 0; i < closes.Count; i++)
        {
            var from = Math.Max(0, i - period + 1);
            var ready = i + 1 >= period;
            double value;
            switch (kind)
            {
                case OracleTrailKind.Atr:
                    value = ready ? closes.Skip(from).Take(i - from + 1).Average(c => c.High - c.Low) : double.NaN;
                    break;
                case OracleTrailKind.Structure:
                    value = ready ? (side == Side.Buy ? closes.Skip(from).Take(i - from + 1).Min(c => c.Low) : closes.Skip(from).Take(i - from + 1).Max(c => c.High)) : double.NaN;
                    break;
                default:
                    ema = double.IsNaN(ema) ? closes[i].Close : ema + ((2.0 / (period + 1)) * (closes[i].Close - ema));
                    value = ready ? ema : double.NaN;
                    break;
            }

            tape.Append(closes[i].Epoch, closes[i].Close, [value]);
            bars.Add(new OracleTrailBar(closes[i].Epoch, closes[i].Close, value));
        }

        IExitRule rule = kind switch
        {
            OracleTrailKind.Atr => new AtrTrailRule(0, (decimal)multiple, (decimal)activateAtRr),
            OracleTrailKind.Structure => new StructureTrailRule(0),
            _ => new EmaTrailRule(0),
        };
        return new Built(new ExitContext(rule, tape), new OracleTrail(kind, bars, multiple, activateAtRr), tape);
    }
}
