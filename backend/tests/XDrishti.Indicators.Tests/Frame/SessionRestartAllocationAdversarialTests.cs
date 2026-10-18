using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Frame;

/// <summary>
/// ISS-13, adversarial: a frame with the twelve names on all six intraday lanes, each spelled twice (a second handle on the same instance), beside a continuous feature on every lane and the 1D session names, allocates
/// exactly 0 bytes over 100,000 steps of a sparse series (2 to 12 bars a day, so late buckets, double closes and pending lanes are the rule), with and without a calendar and with the ordering guard skipping bars.
/// </summary>
[Collection(ExclusiveRun.Name)]
public sealed class SessionRestartAllocationAdversarialTests
{
    private const int WarmUpSteps = 5_000;

    private const int MeasuredSteps = 100_000;

    private static readonly Bar[] Bars = [.. SeriesGenerator.Thin(5, 30_000).Bars];

    private static readonly string[] DayNames = ["today_open", "today_high", "today_low", "prev_day_open", "prev_day_close", "gap_pct"];

    private static List<FeatureSpec> Specs()
    {
        var specs = new List<FeatureSpec>();
        foreach (var timeframe in AdversarialFrames.Intraday)
        {
            specs.AddRange(AdversarialFrames.TwelveOn(timeframe));
            specs.AddRange(AdversarialFrames.TwelveOn(timeframe).Select(s => s with { Source = PriceSource.High }));
            specs.Add(AdversarialFrames.TwelveSpec("or_high", timeframe, 3 * Math.Max(1, timeframe.Minutes())));
            specs.Add(AdversarialFrames.Spec("ema", timeframe, 3));
        }

        specs.AddRange(DayNames.Select(n => AdversarialFrames.Spec(n, Timeframe.D1)));
        return specs;
    }

    [Fact]
    public void The_sparse_series_is_long_enough_and_holds_many_date_changes()
    {
        Bars.Length.ShouldBeGreaterThanOrEqualTo(WarmUpSteps + MeasuredSteps);
        Bars.Skip(WarmUpSteps).Take(MeasuredSteps).Select(b => SessionClock.DateOf(b.EpochSeconds)).Distinct().Count().ShouldBeGreaterThanOrEqualTo(250);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Steps_and_reads_of_every_lag_allocate_nothing_across_date_changes_late_closes_and_skipped_bars(bool useCalendar, bool skippedBars)
    {
        var calendar = useCalendar ? CalendarFixtures.For(Bars) : null;
        AllocationProbe.ShouldAllocateNothing(
            window =>
            {
                var frame = AdversarialFrames.Create(Specs(), 8, calendar);
                for (var i = 0; i < WarmUpSteps; i++)
                {
                    frame.Step(Bars[i]);
                }

                var sink = 0.0;
                window.Begin();
                for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
                {
                    frame.Step(Bars[i]);
                    if (skippedBars)
                    {
                        frame.Step(Bars[i - 1]);
                    }

                    for (var h = 0; h < frame.Count; h += 7)
                    {
                        sink += frame.IsReadyAt(h, 1) ? frame.At(h, 1) : 0;
                        sink += frame.IsReady(h) ? frame[h] : 0;
                    }
                }

                window.End();
                double.IsNaN(sink).ShouldBeFalse();
            },
            $"calendar {useCalendar}, skipped bars {skippedBars}");
    }
}
