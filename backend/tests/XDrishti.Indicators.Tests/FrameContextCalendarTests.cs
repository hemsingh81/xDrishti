using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// The two calendar features (P3-01c criteria 6 and 7): <c>day_of_week</c> is the ISO weekday of the India date of the step and <c>is_expiry_day</c> is 1 exactly on the dates the caller gave. Both are read
/// against the platform's own calendar, never against the library's day arithmetic, and both are ready from the first step.
/// </summary>
public sealed class FrameContextCalendarTests
{
    private static readonly DateOnly Monday = new(2026, 3, 2);

    [Fact]
    public void The_weekday_equals_the_independent_weekday_for_every_day_of_2026_to_2030()
    {
        var frame = FrameContextFixtures.Frame(["day_of_week"], null);
        var dates = FrameContextFixtures.Dates(new DateOnly(2026, 1, 1), new DateOnly(2030, 12, 31));
        var random = new Random(2026);
        var previous = double.NaN;

        dates.Count.ShouldBe(1_826);
        foreach (var date in dates)
        {
            // a minute of the India day chosen at random: the weekday never depends on it (the first minute after midnight and the last before it included)
            var minute = date.Day % 3 == 0 ? 0 : date.Day % 3 == 1 ? 1_439 : random.Next(0, 1_440);
            frame.Step(Minutes.Close(date, minute, 100));

            var expected = FrameContextFixtures.IsoWeekday(date);
            frame[0].ShouldBe(expected, date.ToString("yyyy-MM-dd"));
            frame.IsReady(0).ShouldBeTrue(date.ToString("yyyy-MM-dd"));
            Tolerance.Identical(frame.At(0, 1), previous).ShouldBeTrue(date.ToString("yyyy-MM-dd") + ": the previous date's value (NaN on the first)");
            previous = expected;
        }

        frame.Steps.ShouldBe(1_826);
    }

    [Fact]
    public void The_weekday_is_ready_at_the_very_first_step_and_not_before()
    {
        var frame = FrameContextFixtures.Frame(["day_of_week"], null);

        frame.IsReady(0).ShouldBeFalse();
        frame[0].ShouldBeNaN();
        frame.WarmUp(0).ShouldBe(1);
        frame.Step(Minutes.Close(Monday, Minutes.Open, 100));

        frame.IsReady(0).ShouldBeTrue();
        frame[0].ShouldBe(1.0, "2026-03-02 is a Monday");
        frame.IsReadyAt(0, 1).ShouldBeFalse("no earlier date");
    }

    [Fact]
    public void The_weekday_of_a_stream_with_a_saturday_session_and_a_muhurat_sunday_follows_the_india_date_of_every_bar()
    {
        var saturday = 0;
        var sunday = 0;
        foreach (var seed in new[] { 3, 7, 11, 19, 23 })
        {
            var bars = SeriesGenerator.Random(seed, 10_000, 14_000, specialSessions: true);
            var frame = FrameContextFixtures.Frame(["day_of_week"], null);

            foreach (var bar in bars)
            {
                frame.Step(bar);

                var date = FrameContextFixtures.DateOf(bar);
                frame[0].ShouldBe(FrameContextFixtures.IsoWeekday(date), $"seed {seed}, {date:yyyy-MM-dd}");
                saturday += date.DayOfWeek == DayOfWeek.Saturday ? 1 : 0;
                sunday += date.DayOfWeek == DayOfWeek.Sunday ? 1 : 0;
            }
        }

        saturday.ShouldBeGreaterThan(100, "the streams have Saturday sessions");
        sunday.ShouldBeGreaterThan(100, "the streams have Muhurat evenings");
    }

    [Fact]
    public void The_weekday_changes_at_the_first_step_of_a_new_india_date_and_not_at_midnight_utc()
    {
        // India is UTC+05:30: India 05:30 is midnight UTC. A Monday-to-Tuesday run through both midnights.
        var monday = Monday;
        var tuesday = Monday.AddDays(1);
        var steps = new (DateOnly Date, int Minute, int Expected)[]
        {
            (monday, Minutes.At(23, 58), 1),
            (monday, Minutes.At(23, 59), 1),
            (tuesday, Minutes.At(0, 0), 2),
            (tuesday, Minutes.At(0, 1), 2),
            (tuesday, Minutes.At(5, 29), 2),
            (tuesday, Minutes.At(5, 30), 2),
            (tuesday, Minutes.At(5, 31), 2),
            (tuesday, Minutes.At(23, 59), 2),
            (tuesday.AddDays(1), Minutes.At(0, 0), 3),
        };
        var frame = FrameContextFixtures.Frame(["day_of_week"], null);

        foreach (var (date, minute, expected) in steps)
        {
            frame.Step(Minutes.Close(date, minute, 100));

            frame[0].ShouldBe(expected, $"{date:yyyy-MM-dd} {minute / 60:D2}:{minute % 60:D2} India time");
        }

        // the history shows one entry per India date: Monday, Tuesday, Wednesday
        frame.At(0, 1).ShouldBe(2.0);
        frame.At(0, 2).ShouldBe(1.0);
        frame.IsReadyAt(0, 3).ShouldBeFalse();
    }

    [Fact]
    public void The_weekday_of_a_garbage_epoch_beyond_the_range_of_dates_is_still_a_weekday_and_the_expiry_day_is_not_ready()
    {
        var frame = FrameContextFixtures.Frame(["day_of_week", "is_expiry_day", "vix"], FrameContextFixtures.Build(FrameContextFixtures.Consecutive(1, 30), FrameContextFixtures.Small), new HashSet<DateOnly>());

        frame.Step(new Bar(400_000_000_000L, 1, 1, 1, 1, 1));
        frame.Step(new Bar(-400_000_000_000L - 1, 1, 1, 1, 1, 1));

        frame.Steps.ShouldBe(1, "the second bar is before the first and is skipped by the ordering guard");
        frame.IsReady(0).ShouldBeTrue();
        frame[0].ShouldBeInRange(1.0, 7.0);
        frame.IsReady(1).ShouldBeFalse("no date, no expiry day");
        frame.IsReady(2).ShouldBeFalse();

        var early = FrameContextFixtures.Frame(["day_of_week", "is_expiry_day"], null, new HashSet<DateOnly>());
        early.Step(new Bar(-400_000_000_000L, 1, 1, 1, 1, 1));
        early.IsReady(0).ShouldBeTrue();
        early.IsReady(1).ShouldBeFalse();
    }

    [Fact]
    public void The_expiry_day_is_one_exactly_on_the_dates_of_the_set()
    {
        // a Tuesday, a Thursday before a holiday (Friday 2026-04-03 is Good Friday), a Wednesday the expiry moved to, a Saturday with no bars and a Monday with no bars
        var tuesday = new DateOnly(2026, 3, 3);
        var thursdayBeforeHoliday = new DateOnly(2026, 4, 2);
        var wednesday = new DateOnly(2026, 4, 15);
        var saturdayWithoutBars = new DateOnly(2026, 3, 14);
        var mondayWithoutBars = new DateOnly(2026, 3, 16);
        var expiry = new HashSet<DateOnly> { tuesday, thursdayBeforeHoliday, wednesday, saturdayWithoutBars, mondayWithoutBars };
        var frame = FrameContextFixtures.Frame(["is_expiry_day"], null, expiry);
        var datesWithBars = FrameContextFixtures.Dates(Monday, new DateOnly(2026, 4, 30)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && d != mondayWithoutBars).ToList();
        var ones = new List<DateOnly>();

        foreach (var date in datesWithBars)
        {
            foreach (var bar in FrameContextFixtures.OnDates([date], Minutes.Open, Minutes.At(12, 0), Minutes.At(15, 29)))
            {
                frame.Step(bar);

                frame.IsReady(0).ShouldBeTrue();
                frame[0].ShouldBe(expiry.Contains(date) ? 1.0 : 0.0, $"{date:yyyy-MM-dd} {date.DayOfWeek}");
            }

            if (frame[0] == 1.0)
            {
                ones.Add(date);
            }
        }

        ones.ShouldBe([tuesday, thursdayBeforeHoliday, wednesday], "the Saturday and the Monday of the set had no bars, so no step saw them");
    }

    [Fact]
    public void An_empty_set_gives_zero_on_every_date_and_the_expiry_day_is_ready_from_the_first_step()
    {
        var frame = FrameContextFixtures.Frame(["is_expiry_day"], null, new HashSet<DateOnly>());

        frame.IsReady(0).ShouldBeFalse();
        foreach (var bar in FrameContextFixtures.OnDates(FrameContextFixtures.Dates(Monday, Monday.AddDays(20)), Minutes.Open))
        {
            frame.Step(bar);

            frame[0].ShouldBe(0.0);
            frame.IsReady(0).ShouldBeTrue();
        }

        frame.WarmUp(0).ShouldBe(1);
    }

    [Fact]
    public void The_expiry_day_is_taken_once_per_date_and_its_history_is_the_previous_dates()
    {
        var expiry = new HashSet<DateOnly> { Monday.AddDays(1), Monday.AddDays(4) };
        var frame = FrameContextFixtures.Frame(["is_expiry_day"], null, expiry);
        var dates = Enumerable.Range(0, 7).Select(Monday.AddDays).ToList();
        var seen = new List<double>();

        foreach (var date in dates)
        {
            foreach (var bar in FrameContextFixtures.OnDates([date], Minutes.Open, Minutes.Open + 1, Minutes.Open + 2))
            {
                frame.Step(bar);
            }

            seen.Add(frame[0]);
            for (var lag = 0; lag < Math.Min(seen.Count, frame.HistoryDepth); lag++)
            {
                frame.At(0, lag).ShouldBe(seen[^(lag + 1)], $"{date:yyyy-MM-dd} lag {lag}");
            }
        }

        seen.ShouldBe([0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0]);
    }

    [Fact]
    public void The_calendar_features_do_not_need_a_context_and_a_context_does_not_change_them()
    {
        var expiry = new HashSet<DateOnly> { Monday.AddDays(2) };
        var withContext = FrameContextFixtures.Frame(["day_of_week", "is_expiry_day"], FrameContextFixtures.Build(FrameContextFixtures.Consecutive(5, 40), FrameContextFixtures.Small), expiry);
        var without = FrameContextFixtures.Frame(["day_of_week", "is_expiry_day"], null, expiry);

        foreach (var bar in FrameContextFixtures.OnDates(FrameContextFixtures.Dates(Monday, Monday.AddDays(30)), Minutes.Open, Minutes.At(15, 29)))
        {
            withContext.Step(bar);
            without.Step(bar);

            FrameContextFixtures.Bits(withContext).ShouldBe(FrameContextFixtures.Bits(without));
        }
    }

    [Fact]
    public void A_standalone_instance_made_by_the_catalog_has_a_weekday_but_no_expiry_dates_and_no_context()
    {
        IndicatorCatalog.TryGet("day_of_week", out var weekday).ShouldBeTrue();
        IndicatorCatalog.TryGet("is_expiry_day", out var expiry).ShouldBeTrue();
        IndicatorCatalog.TryGet("regime", out var regime).ShouldBeTrue();
        var instances = new[] { weekday.Create(weekday.DefaultSpec()), expiry.Create(expiry.DefaultSpec()), regime.Create(regime.DefaultSpec()) };
        var bar = Minutes.Close(new DateOnly(2026, 3, 6), Minutes.Open, 100);

        foreach (var instance in instances)
        {
            instance.IsReady.ShouldBeFalse();
            instance.Update(bar);
        }

        instances[0].Value.ShouldBe(5.0, "2026-03-06 is a Friday");
        instances[1].IsReady.ShouldBeFalse();
        instances[2].IsReady.ShouldBeFalse();
    }
}
