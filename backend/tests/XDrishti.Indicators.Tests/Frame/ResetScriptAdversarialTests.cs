using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// ISS-13, adversarial, the three hazards of the session rule together: a frame that is reset several times, fed after each reset from a random point of the series (mid-day, one bar before a date change, a date it
/// has seen before), with duplicates and older bars sprinkled in. After every bar, and after every skipped bar, every lag of every feature is what the independent reference says for the bars fed since the last reset.
/// </summary>
public sealed class ResetScriptAdversarialTests
{
    public static TheoryData<Timeframe, int, bool> Cases
    {
        get
        {
            var data = new TheoryData<Timeframe, int, bool>();
            foreach (var timeframe in AdversarialFrames.Intraday)
            {
                foreach (var seed in new[] { 6, 14, 33 })
                {
                    data.Add(timeframe, seed, seed != 14);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void After_any_number_of_resets_mid_day_starts_and_skipped_bars_every_lag_is_the_reference_of_the_bars_fed_since_the_last_reset(Timeframe timeframe, int seed, bool useCalendar)
    {
        var bars = SeriesGenerator.Random(seed, 1_800, 2_600, specialSessions: true);
        var random = new Random(seed);
        var dateChanges = Enumerable.Range(1, bars.Count - 1).Where(i => SessionClock.DateOf(bars[i].EpochSeconds) != SessionClock.DateOf(bars[i - 1].EpochSeconds)).ToList();
        var segments = new List<IReadOnlyList<Bar>>();
        for (var s = 0; s < 5; s++)
        {
            // the starts: anywhere, the first bar of a date, one bar before a date change, and a start on a date a segment before has fed
            var start = s switch
            {
                1 => dateChanges[random.Next(dateChanges.Count)],
                2 => dateChanges[random.Next(dateChanges.Count)] - 1,
                3 => segments.Count > 0 ? bars.FindIndex(b => b.EpochSeconds == segments[0][0].EpochSeconds) + 17 : 0,
                _ => random.Next(0, bars.Count - 400),
            };
            var length = Math.Min(random.Next(250, 900), bars.Count - start);
            segments.Add(bars.GetRange(start, length));
        }

        var specs = TestSpecs.Variants(timeframe).OrderBy(_ => random.Next()).ToList();

        AdversarialReferenceCheck.RunSegments(specs, segments, 8, useCalendar ? CalendarFixtures.Windows(bars) : null, $"{timeframe.ToCode()} seed {seed} calendar {useCalendar}", new Random(seed * 7));
    }
}
