using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// The prefix property of the date lane (P3-01c criterion 9): what a frame reads at a step depends only on the context days <b>before</b> the date of the step and on the bars up to the step. A change to the day
/// dated <i>D</i> or to any later day — its values, its VIX, whether it exists at all — leaves every step up to and including the last minute of <i>D</i> bit-identical, and cutting the bar stream after k steps gives the
/// first k steps of the full run. 220 seeds each; the context days come in two shapes (consecutive weekdays with a VIX on every day, and a series with holidays, VIX gaps and missing breadth).
/// </summary>
public sealed class FrameContextPrefixTests
{
    private static readonly ContextOptions Options = FrameContextFixtures.Small;

    public static TheoryData<int> Seeds() => [.. Enumerable.Range(0, 220)];

    private static List<ContextDay> Days(int seed, int count) =>
        seed % 2 == 0 ? FrameContextFixtures.Consecutive(seed, count) : ContextSeriesGenerator.Random(seed, count, count);

    /// <summary>The same dates with every price scaled, a VIX that no day has and a breadth of 3: a record that cannot be mistaken for the original.</summary>
    private static ContextDay Altered(ContextDay day, double factor) => day with
    {
        IndexHigh = day.IndexHigh * factor,
        IndexLow = day.IndexLow * factor,
        IndexClose = day.IndexClose * factor,
        VixClose = 99.9,
        Breadth = 3,
    };

    private static List<Bar> BarsOn(IEnumerable<DateOnly> dates) => FrameContextFixtures.OnDates(dates, Minutes.Open, Minutes.At(11, 30), Minutes.At(15, 29));

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Changing_the_day_dated_d_or_any_later_day_leaves_every_step_up_to_the_end_of_d_unchanged(int seed)
    {
        var random = new Random(seed);
        var count = random.Next(40, 90);
        var days = Days(seed, count);
        var cut = random.Next(1, count - 1);
        var cutDate = days[cut].Date;

        // the full context; the context that stops before the cut; the context whose record at the cut and every later one is different
        var full = FrameContextFixtures.Build(days, Options);
        var prefix = FrameContextFixtures.Build(days.Take(cut), Options);
        var changed = FrameContextFixtures.Build(days.Take(cut).Concat(days.Skip(cut).Select(d => Altered(d, 1.37))), Options);
        var upToTheCut = BarsOn(FrameContextFixtures.Dates(days[0].Date.AddDays(-2), cutDate));
        var afterTheCut = BarsOn(FrameContextFixtures.Dates(cutDate.AddDays(1), days[cut + 1].Date));

        var traceFull = FrameContextFixtures.Trace(FrameContextFixtures.Frame(TestSpecs.ContextNames, full), upToTheCut);
        var tracePrefix = FrameContextFixtures.Trace(FrameContextFixtures.Frame(TestSpecs.ContextNames, prefix), upToTheCut);
        var traceChanged = FrameContextFixtures.Trace(FrameContextFixtures.Frame(TestSpecs.ContextNames, changed), upToTheCut);

        tracePrefix.ShouldBe(traceFull, $"seed {seed}: cut after {cut} days, the context without the days from the cut on");
        traceChanged.ShouldBe(traceFull, $"seed {seed}: cut at {cutDate:yyyy-MM-dd}, the days from the cut on changed");

        // the change is real: once the record of the cut becomes visible (the next record's date) the VIX the frame reads is the changed one
        var fullFrame = FrameContextFixtures.Frame(TestSpecs.ContextNames, full);
        var changedFrame = FrameContextFixtures.Frame(TestSpecs.ContextNames, changed);
        FrameContextFixtures.Trace(fullFrame, upToTheCut.Concat(afterTheCut));
        FrameContextFixtures.Trace(changedFrame, upToTheCut.Concat(afterTheCut));
        var vix = fullFrame.HandleOf(new FeatureSpec("vix"));
        changedFrame[vix].ShouldBe(99.9, $"seed {seed}: the frame of the changed context reads its altered record on the next record's date");
        Tolerance.Identical(fullFrame[vix], changedFrame[vix]).ShouldBeFalse($"seed {seed}: the original record is not the altered one");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Cutting_the_bar_stream_after_k_steps_gives_the_first_k_steps_of_the_full_run(int seed)
    {
        var random = new Random(seed);
        var count = random.Next(40, 90);
        var days = Days(seed, count);
        var context = FrameContextFixtures.Build(days, Options);
        var bars = BarsOn(FrameContextFixtures.Dates(days[0].Date.AddDays(-2), days[^1].Date.AddDays(3)));
        var k = random.Next(1, bars.Count);

        var full = FrameContextFixtures.Trace(FrameContextFixtures.Frame(TestSpecs.ContextNames, context), bars);
        var cut = FrameContextFixtures.Trace(FrameContextFixtures.Frame(TestSpecs.ContextNames, context), bars.Take(k));

        cut.Count.ShouldBe(k * TestSpecs.ContextNames.Count * 3);
        cut.ShouldBe(full.Take(cut.Count), $"seed {seed}: cut after {k} of {bars.Count} steps");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_frame_that_starts_later_reads_the_same_values_as_one_that_started_earlier_from_its_first_date_on(int seed)
    {
        var random = new Random(seed);
        var count = random.Next(40, 90);
        var days = Days(seed, count);
        var context = FrameContextFixtures.Build(days, Options);
        var dates = FrameContextFixtures.Dates(days[0].Date.AddDays(-2), days[^1].Date.AddDays(3));
        var bars = BarsOn(dates);
        var startDate = dates[random.Next(0, dates.Count)];
        var early = FrameContextFixtures.Frame(TestSpecs.ContextNames, context);
        var late = FrameContextFixtures.Frame(TestSpecs.ContextNames, context);

        foreach (var bar in bars)
        {
            early.Step(bar);
            if (FrameContextFixtures.DateOf(bar) >= startDate)
            {
                late.Step(bar);

                for (var h = 0; h < early.Count; h++)
                {
                    // the value of the date; the history of the frame that started later is shorter, so only lag 0 is compared
                    Tolerance.Identical(early[h], late[h]).ShouldBeTrue($"seed {seed} {TestSpecs.ContextNames[h]} on {FrameContextFixtures.DateOf(bar):yyyy-MM-dd}");
                }
            }
        }
    }
}
