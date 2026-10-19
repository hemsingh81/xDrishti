using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The counters of the fixture day alone: the scanner runs the prefix, then the day, and the figures are the difference (the prefix sessions are counted too in a scanner's totals).</summary>
internal sealed class DayScan
{
    private DayScan(EntryScanner scanner, ScanCounters before)
    {
        Scanner = scanner;
        var after = scanner.Counters;
        Bars = after.Bars - before.Bars;
        DecisionSteps = after.DecisionSteps - before.DecisionSteps;
        StaleBucketSteps = after.StaleBucketSteps - before.StaleBucketSteps;
        OutOfWindowSteps = after.OutOfWindowSteps - before.OutOfWindowSteps;
        WindowSteps = after.WindowSteps - before.WindowSteps;
        FullyReadySteps = after.FullyReadySteps - before.FullyReadySteps;
        Candidates = after.Candidates - before.Candidates;
        LaterTriggers = after.LaterTriggers - before.LaterTriggers;
        BlockedByInvalidation = after.BlockedByInvalidation - before.BlockedByInvalidation;
        InvalidatedDays = after.InvalidatedDays - before.InvalidatedDays;
        StopNotReady = after.StopNotReady - before.StopNotReady;
        UnknownInvalidation = after.UnknownInvalidation - before.UnknownInvalidation;
        Conditions = after.Conditions.ToDictionary(
            c => c.Path,
            c =>
            {
                var b = before.Conditions.FirstOrDefault(x => x.Path == c.Path);
                return (c.True - (b?.True ?? 0), c.False - (b?.False ?? 0), c.Unknown - (b?.Unknown ?? 0), c.NotReady - (b?.NotReady ?? 0));
            });
    }

    public EntryScanner Scanner { get; }

    public long Bars { get; }

    public long DecisionSteps { get; }

    public long StaleBucketSteps { get; }

    public long OutOfWindowSteps { get; }

    public long WindowSteps { get; }

    public long FullyReadySteps { get; }

    public long Candidates { get; }

    public long LaterTriggers { get; }

    public long BlockedByInvalidation { get; }

    public long InvalidatedDays { get; }

    public long StopNotReady { get; }

    public long UnknownInvalidation { get; }

    /// <summary>Per condition path: (True, False, Unknown, NotReady) on the fixture day alone.</summary>
    public IReadOnlyDictionary<string, (long True, long False, long Unknown, long NotReady)> Conditions { get; }

    /// <summary>Scans the bars; the day starts at index <paramref name="dayStart"/>.</summary>
    public static DayScan Of(StrategyDefinition definition, Side side, Bar[] bars, int dayStart, Timeframe timeframe = Timeframe.M15)
    {
        var scanner = Scan.Scanner(Scan.Compile(definition, side, timeframe));
        for (var i = 0; i < dayStart; i++)
        {
            scanner.Step(bars[i]);
        }

        var before = scanner.Counters;
        for (var i = dayStart; i < bars.Length; i++)
        {
            scanner.Step(bars[i]);
        }

        return new DayScan(scanner, before);
    }

    /// <summary>The scan of a list whose last session (375 minutes, or fewer when it is cut) is the fixture day, the rest the prefix: the day starts at the first bar of the date.</summary>
    public static DayScan OfDay(StrategyDefinition definition, Side side, Bar[] bars, DateOnly date, Timeframe timeframe = Timeframe.M15) =>
        Of(definition, side, bars, Array.FindIndex(bars, b => b.EpochSeconds == Day.Epoch(date, 9 * 60 + 15)), timeframe);
}
