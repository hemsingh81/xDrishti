using System.Diagnostics;
using XDrishti.Application.Portfolio.Returns;
using static XDrishti.Application.Tests.Portfolio.History.HistoryData;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>
/// The calculator alone on 300 holdings over about 520 trading days (P2-07). It runs in a few tens of milliseconds on a developer machine; the bound is 2 seconds on purpose, because it is only there to catch
/// something grossly wrong (a quadratic loop), never to time the code.
/// </summary>
public sealed class PortfolioHistoryCalculatorPerformanceTests
{
    [Fact]
    public void Three_hundred_holdings_over_two_years_are_computed_well_within_a_generous_bound()
    {
        var calendar = Weekdays;
        var days = new List<DateOnly>();
        for (var date = new DateOnly(2024, 10, 14); date <= new DateOnly(2026, 10, 9); date = date.AddDays(1))
        {
            if (calendar.IsTradingDay(date))
            {
                days.Add(date);
            }
        }

        var closes = new Dictionary<Guid, IReadOnlyList<DailyClose>>();
        var holdings = new List<XDrishti.Application.Abstractions.Portfolio.HoldingData>();
        for (var i = 0; i < 300; i++)
        {
            var id = Guid.NewGuid();
            closes[id] = [.. days.Select((d, n) => new DailyClose(d, 100m + ((n + i) % 37)))];
            holdings.Add(Holding(id, HoldingFixtures.Open(10 + i, 100m, days[0]), HoldingFixtures.Open(5, 120m, days[200]), HoldingFixtures.Closed(3, 90m, days[10], days[300], 130m)));
        }

        var index = days.Select((d, n) => new DailyClose(d, 20000m + (n * 3m))).ToList();

        var started = Stopwatch.GetTimestamp();
        var series = PortfolioHistoryCalculator.Build(new HistoryInput(holdings, closes, index, calendar, null));
        var elapsed = Stopwatch.GetElapsedTime(started);

        series.Points.Count.ShouldBe(days.Count);
        series.Coverage.Covered.ShouldBe(300);
        series.Points.ShouldAllBe(p => p.BenchmarkValue != null);
        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }
}
