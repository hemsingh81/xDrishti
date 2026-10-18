using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// The bars the replay feeds (spec P3-04, Scope 3 and 5): the bars inside the windows of the sessions that run, adjusted for the applied corporate actions on a copy, in one list that the scanner and the walker of every segment read. The raw list
/// is never changed. An instrument with no applied action that changes the bars read (<c>ActionsAfter(date of the first bar)</c> empty) whose bars are all fed is not copied: the raw list is the list.
/// </summary>
internal sealed class ReplayBars
{
    private ReplayBars(IReadOnlyList<Bar> fed, int[] sessionStarts)
    {
        Fed = fed;
        SessionStarts = sessionStarts;
    }

    /// <summary>The fed bars, adjusted, in order.</summary>
    public IReadOnlyList<Bar> Fed { get; }

    /// <summary>Position in <see cref="Fed"/> of the first bar of each session that runs, plus the count at the end (so session <c>k</c> is <c>[SessionStarts[k], SessionStarts[k + 1])</c>).</summary>
    public int[] SessionStarts { get; }

    /// <summary>
    /// Builds the list. Each bar is adjusted by <c>AdjustmentFactors.FactorAt</c> of its India date (prices <c>AdjustmentFactors.Price</c>, volume <c>AdjustmentFactors.Volume</c>, 4 decimals); a bar whose factor is 1 (on or after every
    /// ex-date) is copied as it is.
    /// </summary>
    public static ReplayBars Build(IReadOnlyList<Bar> raw, IReadOnlyList<RanSession> ran, AdjustmentFactors factors)
    {
        var starts = new int[ran.Count + 1];
        var total = 0;
        for (var k = 0; k < ran.Count; k++)
        {
            starts[k] = total;
            total += ran[k].Count;
        }

        starts[ran.Count] = total;
        var adjusts = raw.Count > 0 && factors.ActionsAfter(SessionClock.DateOf(raw[0].EpochSeconds)).Count > 0;
        if (!adjusts && total == raw.Count)
        {
            return new ReplayBars(raw, starts);
        }

        var fed = new Bar[total];
        for (var k = 0; k < ran.Count; k++)
        {
            var session = ran[k];
            var factor = adjusts ? factors.FactorAt(session.Date) : 1.0;
            var at = starts[k];
            for (var i = session.Start; i < session.End; i++)
            {
                fed[at++] = factor == 1.0 ? raw[i] : Adjusted(raw[i], factor);
            }
        }

        return new ReplayBars(fed, starts);
    }

    private static Bar Adjusted(Bar bar, double factor) => new(
        bar.EpochSeconds,
        AdjustmentFactors.Price(bar.Open, factor),
        AdjustmentFactors.Price(bar.High, factor),
        AdjustmentFactors.Price(bar.Low, factor),
        AdjustmentFactors.Price(bar.Close, factor),
        AdjustmentFactors.Volume(bar.Volume, factor));
}
