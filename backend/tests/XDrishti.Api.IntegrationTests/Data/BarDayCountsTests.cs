using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The per-day bar counts of the gap check come from the daily aggregate's <c>bars</c> column, and from the 1-minute table only for the days of the instrument's changed range (ADR 0017 §3):
/// the test plants numbers that only one of the two sources can have produced.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class BarDayCountsTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly DateOnly Day1 = new(2026, 10, 7);
    private static readonly DateOnly Day2 = new(2026, 10, 8);
    private static readonly DateOnly Day3 = new(2026, 10, 9);

    private static IEnumerable<(DateOnly Day, int Bars)> Sorted(IReadOnlyDictionary<DateOnly, int> counts) => counts.OrderBy(c => c.Key).Select(c => (c.Key, c.Value));

    private async Task<IReadOnlyDictionary<DateOnly, int>> CountAsync(Guid id, DateOnly from, DateOnly to)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IBarDayCounts>().CountAsync(id, from, to, Ct);
    }

    private static async Task PlantAggregateAsync(NpgsqlConnection connection, Guid id, DateOnly day, int bars) =>
        await ExecuteAsync(
            connection,
            "INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES (@id, '1D', to_timestamp(@ts), 100, 101, 99, 100, 5, @bars, @complete)",
            ("id", id), ("ts", (double)BarFixtures.Epoch(day, 0)), ("bars", bars), ("complete", bars >= 375));

    private static async Task PlantMinutesAsync(NpgsqlConnection connection, Guid id, DateOnly day, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (@id, to_timestamp(@ts), 100, 101, 99, 100, 5, 'dhan')",
                ("id", id), ("ts", (double)BarFixtures.Epoch(day, 9 * 60 + 15 + i)));
        }
    }

    private static async Task PlantCoverageAsync(NpgsqlConnection connection, Guid id, DateOnly? dirtyFrom, DateOnly? dirtyTo) =>
        await ExecuteAsync(
            connection,
            """
            INSERT INTO market.bar_coverage (instrument_id, first_bar, last_bar, bars, csv_bars, dhan_bars, updated_at, dirty_from, dirty_to, aggregates)
            VALUES (@id, to_timestamp(@first), to_timestamp(@last), 10, 0, 10, now(), to_timestamp(@dirtyFrom), to_timestamp(@dirtyTo), '{}'::jsonb)
            """,
            ("id", id),
            ("first", (double)BarFixtures.Epoch(Day1, 9 * 60 + 15)),
            ("last", (double)BarFixtures.Epoch(Day3, 15 * 60 + 29)),
            ("dirtyFrom", dirtyFrom is { } f ? BarFixtures.Epoch(f, 9 * 60 + 15) : DBNull.Value),
            ("dirtyTo", dirtyTo is { } t ? BarFixtures.Epoch(t, 15 * 60 + 29) : DBNull.Value));

    [Fact]
    public async Task With_current_aggregates_the_counts_are_the_daily_aggregates_and_no_minute_is_read()
    {
        var id = await SeedAsync("ABB");
        await using var connection = await OpenAsync();
        await PlantAggregateAsync(connection, id, Day1, 375);
        await PlantAggregateAsync(connection, id, Day2, 200);
        await PlantAggregateAsync(connection, id, Day3, 375);
        await PlantMinutesAsync(connection, id, Day1, 10); // contradicts the aggregate: if the minutes were counted, Day1 would show 10
        await PlantCoverageAsync(connection, id, null, null);

        var counts = await CountAsync(id, Day1, Day3);

        Sorted(counts).ShouldBe(new[] { (Day1, 375), (Day2, 200), (Day3, 375) });
    }

    [Fact]
    public async Task Days_in_the_changed_range_are_counted_from_the_minutes_and_the_others_still_from_the_aggregate()
    {
        var id = await SeedAsync("ABB");
        await using var connection = await OpenAsync();
        await PlantAggregateAsync(connection, id, Day1, 375);
        await PlantAggregateAsync(connection, id, Day2, 200); // stale: bars were merged since
        await PlantAggregateAsync(connection, id, Day3, 375);
        await PlantMinutesAsync(connection, id, Day2, 50);
        await PlantMinutesAsync(connection, id, Day1, 10);
        await PlantCoverageAsync(connection, id, Day2, Day2);

        var counts = await CountAsync(id, Day1, Day3);

        Sorted(counts).ShouldBe(new[] { (Day1, 375), (Day2, 50), (Day3, 375) });
    }

    [Fact]
    public async Task A_changed_day_without_an_aggregate_yet_is_counted_from_the_minutes_and_a_day_without_bars_is_absent()
    {
        var id = await SeedAsync("ABB");
        await using var connection = await OpenAsync();
        await PlantMinutesAsync(connection, id, Day3, 30);
        await PlantCoverageAsync(connection, id, Day3, Day3);

        var counts = await CountAsync(id, Day1, Day3);

        Sorted(counts).ShouldBe(new[] { (Day3, 30) });
    }

    [Fact]
    public async Task Minutes_are_counted_by_the_india_day_and_the_asked_range_bounds_the_answer()
    {
        var id = await SeedAsync("ABB");
        await using var connection = await OpenAsync();
        await PlantMinutesAsync(connection, id, Day1, 5);
        await ExecuteAsync(connection, "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (@id, to_timestamp(@ts), 1, 2, 1, 2, 5, 'csv')", ("id", id), ("ts", (double)BarFixtures.Epoch(Day2, 0) + 60)); // 00:01 India: still Day2
        await PlantCoverageAsync(connection, id, Day1, Day3);

        var wide = await CountAsync(id, Day1, Day3);
        var narrow = await CountAsync(id, Day2, Day2);

        Sorted(wide).ShouldBe(new[] { (Day1, 5), (Day2, 1) });
        Sorted(narrow).ShouldBe(new[] { (Day2, 1) });
        (await CountAsync(id, Day3, Day1)).ShouldBeEmpty();
    }
}
