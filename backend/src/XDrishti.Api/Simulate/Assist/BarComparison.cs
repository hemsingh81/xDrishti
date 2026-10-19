using System.Globalization;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>One minute of the comparison: its India time (HH:mm) and the words of spec P3-09c D3.</summary>
internal sealed record MinuteComparison(string Minute, string Words, bool Matches);

/// <summary>
/// The bar comparison (spec P3-09c D3), pure. A minute is <c>match</c> when open, high, low and close are equal, <c>differs by N ticks</c> when the largest difference is N ticks (rounded up), <c>missing in stored</c> or
/// <c>missing in second source</c> when one side has no bar. The 09:15 bar names its known cause first (ISS-21). A minute that neither side has is not reported.
/// </summary>
internal static class BarComparison
{
    public const string Iss21 = "ISS-21: the importer can widen the opening bar";

    private const int IndiaOffsetSeconds = 19800;

    public static IReadOnlyList<MinuteComparison> Compare(IReadOnlyList<ReaderBar> stored, IReadOnlyList<ReaderBar> second, IReadOnlyCollection<long> epochs, decimal tick)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(epochs);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tick, 0m);

        var storedBy = stored.ToDictionary(b => b.EpochSeconds);
        var secondBy = second.ToDictionary(b => b.EpochSeconds);
        var result = new List<MinuteComparison>();
        foreach (var epoch in epochs.Distinct().Order())
        {
            storedBy.TryGetValue(epoch, out var a);
            secondBy.TryGetValue(epoch, out var b);
            if (a is null && b is null)
            {
                continue;
            }

            var label = Label(epoch);
            if (a is null)
            {
                result.Add(new MinuteComparison(label, "missing in stored", false));
            }
            else if (b is null)
            {
                result.Add(new MinuteComparison(label, "missing in second source", false));
            }
            else
            {
                var largest = new[] { a.Open - b.Open, a.High - b.High, a.Low - b.Low, a.Close - b.Close }.Max(Math.Abs);
                if (largest == 0m)
                {
                    result.Add(new MinuteComparison(label, "match", true));
                    continue;
                }

                var ticks = Math.Ceiling(largest / tick);
                var words = "differs by " + ticks.ToString(CultureInfo.InvariantCulture) + (ticks == 1m ? " tick" : " ticks");
                result.Add(new MinuteComparison(label, label == "09:15" ? Iss21 + "; " + words : words, false));
            }
        }

        return result;
    }

    /// <summary>The minutes that matter for a reading: the opening range, the trigger bar, the entry and the exit minute, each with the minute before and after.</summary>
    public static IReadOnlyCollection<long> Minutes(long dayOpenEpoch, ReaderStrategy strategy, ReaderReading? reading)
    {
        var epochs = new SortedSet<long>();
        for (var i = 0; i < strategy.OrMinutes; i++)
        {
            epochs.Add(dayOpenEpoch + (i * 60L));
        }

        if (reading is not null)
        {
            for (var i = -1; i <= strategy.EvaluationMinutes; i++)
            {
                epochs.Add(reading.TriggerBarStartEpoch + (i * 60L));
            }

            foreach (var centre in new[] { reading.EntryEpoch, reading.ExitEpoch })
            {
                for (var i = -1; i <= 1; i++)
                {
                    epochs.Add(centre + (i * 60L));
                }
            }
        }

        return epochs;
    }

    public static string Label(long epoch) => TimeSpan.FromSeconds(((epoch + IndiaOffsetSeconds) % 86400 + 86400) % 86400).ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}
