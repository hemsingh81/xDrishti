using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>The weekly and monthly builders allocate nothing in steady state, with and without a calendar, on a clean series and on one with gaps, holidays, short days and weekend sessions (measured with <see cref="AllocationProbe"/>, which a collection on another thread cannot disturb).</summary>
[Collection(ExclusiveRun.Name)]
public sealed class WeeklyMonthlyBuilderAllocationTests
{
    private const int WarmUpSteps = 20_000;

    private const int MeasuredSteps = 100_000;

    private static readonly Bar[] CleanBars = [.. SeriesGenerator.Sessions(99, 330)];

    private static readonly PeriodSeries Gappy = BarFixtures.RandomPeriods(11, 760);

    [Fact]
    public void The_series_are_long_enough_and_cover_several_weeks_and_months_in_the_measured_window()
    {
        CleanBars.Length.ShouldBeGreaterThanOrEqualTo(WarmUpSteps + MeasuredSteps);
        Gappy.Bars.Count.ShouldBeGreaterThanOrEqualTo(WarmUpSteps + MeasuredSteps);
        foreach (var timeframe in new[] { Timeframe.W1, Timeframe.Mo1 })
        {
            var run = BuilderRun.Run(Gappy.Bars, timeframe, new TimeframeBuilder(timeframe, CalendarFixtures.For(Gappy.Bars, Gappy.Holidays)));
            run.Count(x => x.Step >= WarmUpSteps && x.Step < WarmUpSteps + MeasuredSteps).ShouldBeGreaterThan(timeframe == Timeframe.W1 ? 20 : 4);
        }
    }

    [Fact]
    public void The_weekly_and_monthly_builders_allocate_nothing()
    {
        foreach (var timeframe in new[] { Timeframe.W1, Timeframe.Mo1 })
        {
            foreach (var calendar in new TradingCalendar?[] { null, new TradingCalendar() })
            {
                Measure(CleanBars, timeframe, calendar, $"clean, {timeframe.ToCode()}, calendar {calendar is not null}");
            }
        }
    }

    [Fact]
    public void The_weekly_and_monthly_builders_allocate_nothing_on_the_gappy_series_with_and_without_a_calendar()
    {
        var bars = Gappy.Bars.ToArray();
        foreach (var timeframe in new[] { Timeframe.W1, Timeframe.Mo1 })
        {
            foreach (var calendar in new TradingCalendar?[]
            {
                null,
                CalendarFixtures.For(Gappy.Bars, Gappy.Holidays),
                CalendarFixtures.For(CalendarFixtures.WeekdayWindows(Gappy.Bars), Gappy.Holidays),
                CalendarFixtures.For(new Dictionary<DateOnly, (int From, int To)>(), Gappy.Holidays),
            })
            {
                Measure(bars, timeframe, calendar, $"gappy, {timeframe.ToCode()}, calendar {calendar is not null}");
            }
        }
    }

    private static void Measure(Bar[] bars, Timeframe timeframe, TradingCalendar? calendar, string label) => AllocationProbe.ShouldAllocateNothing(window =>
    {
        var builder = new TimeframeBuilder(timeframe, calendar);
        for (var i = 0; i < WarmUpSteps; i++)
        {
            builder.Push(bars[i], out _, out _);
        }

        window.Begin();
        for (var i = WarmUpSteps; i < WarmUpSteps + MeasuredSteps; i++)
        {
            builder.Push(bars[i], out _, out _);
        }

        window.End();
    }, label);
}
