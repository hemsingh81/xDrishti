using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Domain.Tests.Market.Context;

/// <summary>P3-01d criterion 7 and the health of the stored context (criterion 6): first readable dates are the day after the Nth record.</summary>
public sealed class ContextCoverageTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);

    private static List<ContextDayRecord> Weekdays(int count, Func<int, bool> hasVix)
    {
        var records = new List<ContextDayRecord>();
        var date = Start;
        while (records.Count < count)
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                var i = records.Count;
                records.Add(new ContextDayRecord(date, 100 + i + 1, 100 + i - 1, 100 + i, hasVix(i) ? 15 : null, 375, hasVix(i) ? 375 : null));
            }

            date = date.AddDays(1);
        }

        return records;
    }

    [Fact]
    public void An_empty_store_has_nothing_readable()
    {
        var coverage = ContextCoverage.Of([]);

        coverage.ShouldBe(new ContextCoverage(0, null, null, null, null, null, null, 0, 0, null, false));
    }

    [Theory]
    [InlineData(54, false)]
    [InlineData(55, true)]
    public void The_index_trend_is_readable_the_day_after_the_55th_record(int days, bool readable)
    {
        var records = Weekdays(days, _ => true);

        var coverage = ContextCoverage.Of(records);

        coverage.IndexTrendReadableFrom.ShouldBe(readable ? records[54].Date.AddDays(1) : null);
    }

    [Theory]
    [InlineData(264, false)]
    [InlineData(265, true)]
    public void The_regime_is_readable_the_day_after_the_265th_record(int days, bool readable)
    {
        var records = Weekdays(days, _ => true);

        ContextCoverage.Of(records).RegimeReadableFrom.ShouldBe(readable ? records[264].Date.AddDays(1) : null);
    }

    [Fact]
    public void The_vix_percentile_needs_252_days_that_have_a_vix_and_a_gap_day_does_not_count()
    {
        var records = Weekdays(300, i => i % 10 != 5);

        var coverage = ContextCoverage.Of(records);

        var vixDates = records.Where(r => r.VixClose is not null).Select(r => r.Date).ToList();
        coverage.VixDays.ShouldBe(vixDates.Count);
        coverage.VixMissingDays.ShouldBe(300 - vixDates.Count);
        coverage.VixPctReadableFrom.ShouldBe(vixDates[251].AddDays(1));
        coverage.VixWindowSpanDays.ShouldBe(vixDates[^1].DayNumber - vixDates[^252].DayNumber);
    }

    [Fact]
    public void The_regime_has_both_parts_once_265_records_and_252_vix_days_are_there_and_the_day_has_a_vix()
    {
        var records = Weekdays(300, _ => true);

        ContextCoverage.Of(records).RegimeWithVixReadableFrom.ShouldBe(records[264].Date.AddDays(1));
        ContextCoverage.Of(Weekdays(300, i => i >= 100)).RegimeWithVixReadableFrom.ShouldBeNull("only 200 VIX days");
        ContextCoverage.Of(Weekdays(300, i => i >= 20)).RegimeWithVixReadableFrom.ShouldBe(records[271].Date.AddDays(1), "the 252nd VIX day is record 272");
    }

    [Fact]
    public void A_window_of_vix_days_wider_than_400_calendar_days_is_flagged()
    {
        var records = Weekdays(300, _ => true);
        var stretched = records.Select((r, i) => i < 150 ? r : r with { Date = r.Date.AddDays(200) }).ToList();

        var coverage = ContextCoverage.Of(stretched);

        coverage.VixWindowSpanDays!.Value.ShouldBeGreaterThan(400);
        coverage.VixWindowTooWide.ShouldBeTrue();
        ContextCoverage.Of(records).VixWindowTooWide.ShouldBeFalse();
    }

    [Fact]
    public void Health_counts_the_sessions_behind_the_last_closed_one_and_the_holes_inside_the_stored_range()
    {
        var calendar = new TradingCalendar();
        var stored = new HashSet<DateOnly> { new(2026, 3, 2), new(2026, 3, 3), new(2026, 3, 5), new(2026, 3, 6) };

        var health = ContextHealth.Of(stored, calendar, new DateOnly(2026, 3, 11))!;

        health.ShouldBe(new ContextHealth(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), 3, 1));
        health.Stale.ShouldBeTrue();
        ContextHealth.Of(stored, calendar, new DateOnly(2026, 3, 9))!.Stale.ShouldBeFalse("one session behind is current: the end-of-day run writes the closed session the same afternoon");
        ContextHealth.Of(new HashSet<DateOnly>(), calendar, new DateOnly(2026, 3, 9)).ShouldBeNull();
    }
}
