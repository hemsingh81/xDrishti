using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>
/// Criterion 9a and 13: <c>TryAsOf(D)</c> returns the latest day <b>strictly before</b> D — a record dated D is in the context and is never read for D — across weekends and holidays, before the first record, and
/// for any number of readers; the cursor overload gives the same answer from any starting hint and costs O(1) for a date a day later. Determinism of the build.
/// </summary>
public sealed class MarketContextTests
{
    private static readonly ContextOptions Small = new(TrendSmaPeriod: 8, TrendSlopeDays: 3, AdxPeriod: 5, AdxMin: 20, AtrPeriod: 5, PercentileWindow: 20);

    public static TheoryData<int> Seeds { get; } = [.. Enumerable.Range(1, 20)];

    private static ContextDay Day(DateOnly date, double close, double? vix = null) => new(date, close + 1, close - 1, close, vix);

    [Fact]
    public void A_record_dated_D_is_in_the_context_but_TryAsOf_D_returns_the_day_before()
    {
        var monday = new DateOnly(2026, 3, 2);
        var context = MarketContext.Create([Day(monday, 100, 15), Day(monday.AddDays(1), 101, 16), Day(monday.AddDays(2), 102, 17)]).Value;

        context.Count.ShouldBe(3);
        context.TryAsOf(monday.AddDays(2), out var forWednesday).ShouldBeTrue();
        context.TryAsOf(monday.AddDays(3), out var forThursday).ShouldBeTrue();

        forWednesday.Date.ShouldBe(monday.AddDays(1), "the Wednesday record is present, the Wednesday reader sees Tuesday");
        forWednesday.Vix.ShouldBe(16);
        forThursday.Date.ShouldBe(monday.AddDays(2));
        forThursday.Vix.ShouldBe(17);
    }

    [Fact]
    public void Nothing_is_returned_on_or_before_the_first_record_or_from_an_empty_context()
    {
        var first = new DateOnly(2026, 3, 2);
        var context = MarketContext.Create([Day(first, 100), Day(first.AddDays(1), 101)]).Value;

        context.TryAsOf(first, out var onFirst).ShouldBeFalse();
        context.TryAsOf(first.AddDays(-1), out var before).ShouldBeFalse();
        context.TryAsOf(DateOnly.MinValue, out _).ShouldBeFalse();
        context.TryAsOf(first.AddDays(1), out var afterFirst).ShouldBeTrue();
        MarketContext.Create([]).Value.TryAsOf(DateOnly.MaxValue, out _).ShouldBeFalse();

        onFirst.ShouldBe(default);
        before.ShouldBe(default);
        afterFirst.Date.ShouldBe(first);
    }

    [Fact]
    public void A_weekend_or_a_holiday_gap_maps_to_the_previous_record()
    {
        var friday = new DateOnly(2026, 3, 6);
        var monday = friday.AddDays(3);
        var thursdayBeforeHoliday = new DateOnly(2026, 3, 19);
        var context = MarketContext.Create([Day(friday, 100), Day(monday, 101), Day(thursdayBeforeHoliday, 102), Day(thursdayBeforeHoliday.AddDays(4), 103)]).Value;

        foreach (var (asked, expected) in new[]
        {
            (friday.AddDays(1), friday),
            (friday.AddDays(2), friday),
            (monday, friday),
            (monday.AddDays(1), monday),
            (thursdayBeforeHoliday, monday),
            (thursdayBeforeHoliday.AddDays(1), thursdayBeforeHoliday),
            (thursdayBeforeHoliday.AddDays(3), thursdayBeforeHoliday),
            (thursdayBeforeHoliday.AddDays(4), thursdayBeforeHoliday),
            (thursdayBeforeHoliday.AddDays(5), thursdayBeforeHoliday.AddDays(4)),
            (DateOnly.MaxValue, thursdayBeforeHoliday.AddDays(4)),
        })
        {
            context.TryAsOf(asked, out var values).ShouldBeTrue($"{asked}");
            values.Date.ShouldBe(expected, $"{asked}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_answer_for_every_date_is_the_latest_record_before_it_and_never_a_later_one(int seed)
    {
        var days = ContextSeriesGenerator.Random(seed, 100, 400);
        var context = MarketContext.Create(days, Small).Value;
        var byDay = ContextRead.Days(context, days);

        for (var k = 0; k < days.Count; k++)
        {
            // asked on the date of record k (and one day later, and in the gap before the next record)
            var onDate = context.TryAsOf(days[k].Date, out var read);
            onDate.ShouldBe(k > 0, $"seed {seed}, day {k}");
            if (k > 0)
            {
                ContextRead.FirstDifference(read, byDay[k - 1]).ShouldBeNull($"seed {seed}, TryAsOf({days[k].Date}) must be the record before it");
            }

            context.TryAsOf(days[k].Date.AddDays(1), out var next).ShouldBeTrue();
            ContextRead.FirstDifference(next, byDay[k]).ShouldBeNull($"seed {seed}, TryAsOf(day after {days[k].Date}) must be that day");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_cursor_overload_gives_the_same_answer_from_any_hint_and_in_any_order(int seed)
    {
        var days = ContextSeriesGenerator.Random(seed, 100, 400);
        var context = MarketContext.Create(days, Small).Value;
        var random = new Random(seed);
        var from = days[0].Date.AddDays(-3);
        var until = days[^1].Date.AddDays(5);
        var span = until.DayNumber - from.DayNumber;

        // a mixed walk: runs of increasing dates, jumps forward, jumps back, repeats, and hints that are nonsense
        var cursor = 0;
        var date = from;
        for (var i = 0; i < 3_000; i++)
        {
            var move = random.Next(10);
            date = move switch
            {
                < 5 => date.AddDays(random.Next(0, 3)),
                < 7 => date.AddDays(random.Next(3, 60)),
                < 9 => date.AddDays(-random.Next(0, 90)),
                _ => from.AddDays(random.Next(0, span + 1)),
            };
            if (date < from || date > until)
            {
                date = from.AddDays(random.Next(0, span + 1));
            }

            if (random.Next(20) == 0)
            {
                cursor = random.Next(-5, days.Count + 50);
            }

            var before = cursor;
            var expected = context.TryAsOf(date, out var plain);
            var actual = context.TryAsOf(date, ref cursor, out var viaCursor);

            actual.ShouldBe(expected, $"seed {seed}, {date}");
            ContextRead.FirstDifference(plain, viaCursor).ShouldBeNull($"seed {seed}, {date}, hint {before}");
            if (actual)
            {
                days[cursor].Date.ShouldBe(viaCursor.Date, "the hint moves to the index of the answer");
            }
            else
            {
                cursor.ShouldBe(before, "a miss leaves the hint alone");
            }
        }
    }

    [Fact]
    public void A_frame_stepping_through_consecutive_dates_moves_the_cursor_by_at_most_one_day_per_date()
    {
        var days = ContextSeriesGenerator.Random(3, 400, 400);
        var context = MarketContext.Create(days, Small).Value;
        var cursor = 0;
        var positions = new List<int>();

        for (var date = days[0].Date.AddDays(1); date <= days[^1].Date.AddDays(1); date = date.AddDays(1))
        {
            context.TryAsOf(date, ref cursor, out _).ShouldBeTrue();
            positions.Add(cursor);
        }

        positions.Zip(positions.Skip(1)).All(p => p.Second - p.First is 0 or 1).ShouldBeTrue();
        positions[^1].ShouldBe(days.Count - 1);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(5_000)]
    public void The_cursor_walk_looks_at_a_few_dates_per_step_whatever_the_length_of_the_context_and_a_jump_falls_back_to_the_binary_search(int length)
    {
        // Amendment 5: the public TryAsOf is a binary search (O(log n)); the O(1) amortised read of criterion 13 is the overload with a hint, and the number of dates it looks at is what proves it.
        var days = ContextSeriesGenerator.Random(5, length, length);
        var context = MarketContext.Create(days, Small).Value;
        var cursor = 0;
        var mostRead = 0;
        var totalRead = 0;
        var steps = 0;

        for (var date = days[0].Date.AddDays(1); date <= days[^1].Date.AddDays(1); date = date.AddDays(1))
        {
            context.TryAsOf(date, ref cursor, out _, out var read).ShouldBeTrue();
            mostRead = Math.Max(mostRead, read);
            totalRead += read;
            steps++;
        }

        mostRead.ShouldBeLessThanOrEqualTo(3, "the date of the hint, the next day (it is not before the date), or one day more when the answer moved on: independent of the length");
        (totalRead / (double)steps).ShouldBeLessThan(3, "two dates when the answer stays (the hint and the next day), three when it moves on; a binary search needs log2(n)");

        // a fresh frame (hint 0) asking for the end, and a frame that jumps back: both search, and a search needs about log2(n) dates
        var log = (int)Math.Ceiling(Math.Log2(length)) + 1;
        var forwardCursor = 0;
        context.TryAsOf(days[^1].Date.AddDays(1), ref forwardCursor, out var last, out var forwardRead).ShouldBeTrue();
        var backCursor = days.Count - 1;
        context.TryAsOf(days[1].Date.AddDays(1), ref backCursor, out var second, out var backRead).ShouldBeTrue();

        last.Date.ShouldBe(days[^1].Date);
        forwardCursor.ShouldBe(days.Count - 1);
        second.Date.ShouldBe(days[1].Date);
        backCursor.ShouldBe(1);
        forwardRead.ShouldBeInRange(Math.Min(4, log - 2), 1 + 4 + log, "the walk of four steps, then the search of the rest");
        backRead.ShouldBeInRange(1, 1 + log);
        if (length >= 5_000)
        {
            forwardRead.ShouldBeGreaterThan(3, "a jump is not an O(1) read");
            backRead.ShouldBeGreaterThan(3);
        }
    }

    [Fact]
    public void The_hint_overload_reads_nothing_on_an_empty_context_and_leaves_the_hint_alone_on_a_miss()
    {
        var empty = MarketContext.Create([], Small).Value;
        var cursor = 7;

        empty.TryAsOf(new DateOnly(2026, 3, 2), ref cursor, out _, out var read).ShouldBeFalse();
        read.ShouldBe(0);
        cursor.ShouldBe(7);

        var one = MarketContext.Create([Day(new DateOnly(2026, 3, 2), 100)], Small).Value;
        cursor = 0;
        one.TryAsOf(new DateOnly(2026, 3, 2), ref cursor, out _, out read).ShouldBeFalse("a record dated D is not read for D");
        cursor.ShouldBe(0);
        read.ShouldBe(1);
    }

    [Fact]
    public void Reading_does_not_change_the_context_so_many_readers_agree_with_a_single_one()
    {
        var days = ContextSeriesGenerator.Random(9, 300, 300);
        var context = MarketContext.Create(days, Small).Value;
        var dates = Enumerable.Range(0, 2_000).Select(i => days[0].Date.AddDays(i % (days[^1].Date.DayNumber - days[0].Date.DayNumber + 3))).ToArray();
        var expected = dates.Select(d => context.TryAsOf(d, out var v) ? v : default).ToArray();
        var disagreements = 0;

        Parallel.For(0, 8, worker =>
        {
            var cursor = 0;
            var random = new Random(worker);
            for (var i = 0; i < dates.Length * 3; i++)
            {
                var index = random.Next(dates.Length);
                var found = worker % 2 == 0 ? context.TryAsOf(dates[index], out var value) : context.TryAsOf(dates[index], ref cursor, out value);
                if (!ContextRead.Identical(found ? value : default, expected[index]))
                {
                    Interlocked.Increment(ref disagreements);
                }
            }
        });

        disagreements.ShouldBe(0);
        ContextRead.Days(context, days).Count.ShouldBe(days.Count);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Building_in_one_call_and_appending_day_by_day_give_the_same_context(int seed)
    {
        var days = ContextSeriesGenerator.Random(seed, 100, 400);
        var built = MarketContext.Create(days, Small).Value;
        var appended = MarketContext.Create([], Small).Value;
        foreach (var day in days)
        {
            appended.Append(day).IsSuccess.ShouldBeTrue();
        }

        appended.Count.ShouldBe(built.Count);
        var a = ContextRead.Days(built, days);
        var b = ContextRead.Days(appended, days);
        for (var i = 0; i < days.Count; i++)
        {
            ContextRead.FirstDifference(a[i], b[i]).ShouldBeNull($"seed {seed}, day {i}");
        }
    }

    [Fact]
    public void Two_builds_of_the_same_days_are_bit_identical_also_on_another_thread()
    {
        var days = ContextSeriesGenerator.Random(21, 500, 500);
        var first = ContextRead.Days(MarketContext.Create(days).Value, days);
        var second = ContextRead.Days(MarketContext.Create(days).Value, days);
        List<ContextValues>? third = null;
        var thread = new Thread(() => third = ContextRead.Days(MarketContext.Create(days).Value, days));
        thread.Start();
        thread.Join();

        for (var i = 0; i < days.Count; i++)
        {
            ContextRead.FirstDifference(first[i], second[i]).ShouldBeNull($"day {i}");
            ContextRead.FirstDifference(first[i], third![i]).ShouldBeNull($"day {i}, other thread");
        }
    }

    [Fact]
    public void The_epoch_given_to_the_daily_bars_starts_at_the_day_number_of_1970_01_01()
    {
        IndiaDays.UnixEpochDayNumber.ShouldBe(new DateOnly(1970, 1, 1).DayNumber);
    }
}
