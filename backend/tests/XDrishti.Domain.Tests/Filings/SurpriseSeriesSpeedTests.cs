using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Calendar;
using static XDrishti.Domain.Tests.Filings.CalendarSamples;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-09, non-functional: the per-date bucket series of 214 instruments over five years (21 seasons of one filing each) is built, breakpoints included. The test asserts the work done, not the clock (a wall-clock bound is flaky under load; the time is a benchmark, measured at about 0.5 s on a quiet machine).</summary>
public sealed class SurpriseSeriesSpeedTests
{
    [Fact]
    public void The_bucket_series_for_214_instruments_over_five_years_is_built_with_buckets_on_the_dates_after_the_second_season()
    {
        var calendar = new TradingCalendar();
        var random = new Random(7);
        var instruments = Enumerable.Range(0, 214).Select(_ => Guid.CreateVersion7()).ToList();
        var quarterEnds = Enumerable.Range(0, 21).Select(q => new DateOnly(2021, 3, 31).AddMonths(3 * q)).Select(d => new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month))).ToList();
        var filings = new Dictionary<Guid, List<ResultsFilingTime>>();
        var observations = new List<SurpriseObservation>();
        foreach (var instrument in instruments)
        {
            filings[instrument] = [];
            foreach (var end in quarterEnds)
            {
                var broadcast = new DateTimeOffset(end.AddDays(25 + random.Next(30)).ToDateTime(new TimeOnly(16, 0)), TimeSpan.FromMinutes(330));
                filings[instrument].Add(new ResultsFilingTime(instrument, end, broadcast));
                observations.Add(new SurpriseObservation(instrument, end, broadcast, random.NextDouble() * 6 - 3, random.NextDouble() - 0.5));
            }
        }

        var inputs = SurpriseInputs.Create(observations, Lag, calendar);
        var points = 0;
        var ranked = 0;
        foreach (var instrument in instruments)
        {
            var series = ResultsDaySeries.Build([], filings[instrument], Lag, calendar, new DateOnly(2021, 1, 1), new DateOnly(2026, 4, 30), inputs).Points;
            points += series.Count;
            ranked += series.Values.Count(p => !double.IsNaN(p.SueQuintile) && p.SueQuintile is >= 1 and <= 5);
        }

        points.ShouldBeGreaterThan(214 * 1_000);
        ranked.ShouldBeGreaterThan(214 * 900, "from the second season on every date has a bucket: the previous season is complete and holds 214 values");
    }
}
