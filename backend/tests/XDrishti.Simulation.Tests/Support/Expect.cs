using XDrishti.Domain.Research;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The hand-made candidates of the specs, written as the specs write them: side, list index, decision clock time, stop and trigger bar. Compared as text without the trace.</summary>
internal static class Expect
{
    private static readonly DateOnly Date = Day.Monday;

    public static string Candidate(Side side, int index, string decision, double stop, string barStart, double open, double high, double low, double close, int minutes, bool complete, string id = "ORB", DateOnly? date = null, XDrishti.Domain.Market.Bars.Timeframe timeframe = XDrishti.Domain.Market.Bars.Timeframe.M15)
    {
        var day = date ?? Date;
        var decisionEpoch = Day.Epoch(day, Day.Minute(decision));
        var bar = new XDrishti.Domain.Market.Bars.Bar(Day.Epoch(day, Day.Minute(barStart)), open, high, low, close, minutes * Day.MinuteVolume);
        return Render.Candidate(new EntryCandidate(id, Scan.Instrument, side, index, decisionEpoch, timeframe, bar, minutes, complete, stop, DecisionTrace.Empty), withTrace: false);
    }

    /// <summary>Cand-B: BUY, index 29, decision 09:45, stop 98.00, trigger bar 09:30 (O 99.00 H 100.50 L 99.00 C 100.50, 15 minutes, complete).</summary>
    public static string CandB() => Candidate(Side.Buy, 29, "09:45", 98.00, "09:30", 99.00, 100.50, 99.00, 100.50, 15, true);
}
