using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.OpeningBars;

namespace XDrishti.Api.IntegrationTests.OpeningBars;

/// <summary>
/// BK-03b criteria 8 and 12 on a real TimescaleDB: the live-bar table is insert-only with a duplicate left alone and counted, the history merges the stored 1-minute bars (first) with earlier live rows, the day's events and the owner's switch (no row = OFF,
/// owner actors only) go in and come back.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OpeningBarsDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, Ist);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        await ExecuteAsync(connection, "TRUNCATE market.bars_1m_live, market.opening_bar_events");
        await ExecuteAsync(connection, "TRUNCATE market.opening_bars_switch RESTART IDENTITY");
    }

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static MinuteBar Bar(Guid id, DateTimeOffset minute, decimal open = 100m, long volume = 500) => new(id, minute, open, open + 2, open - 1, open + 1, volume, 3);

    [Fact]
    public async Task A_bar_is_stored_once_a_second_write_of_the_minute_is_a_duplicate_and_the_first_values_stay()
    {
        var id = Guid.CreateVersion7();
        var first = new[] { Bar(id, At(12, 9, 15)), Bar(id, At(12, 9, 16), 101m) };

        var added = await WithAsync(p => p.GetRequiredService<IOpeningBarsStore>().AppendAsync(first, Ct).AsTask());
        var again = await WithAsync(p => p.GetRequiredService<IOpeningBarsStore>().AppendAsync([Bar(id, At(12, 9, 15), 999m), Bar(id, At(12, 9, 17), 102m)], Ct).AsTask());

        added.ShouldBe(2);
        again.ShouldBe(1, "only the 09:17 minute is new");
        (await ScalarAsync<long>("SELECT count(*) FROM market.bars_1m_live")).ShouldBe(3);
        (await ScalarAsync<decimal>("SELECT o FROM market.bars_1m_live WHERE minute_start = @t", ("t", At(12, 9, 15).ToUniversalTime()))).ShouldBe(100m);
    }

    [Fact]
    public async Task An_empty_batch_writes_nothing_and_a_bar_with_a_bad_price_is_refused_by_the_table()
    {
        var store = (await WithAsync(p => Task.FromResult(p.GetRequiredService<IOpeningBarsStore>())));
        (await store.AppendAsync([], Ct)).ShouldBe(0);

        var bad = new MinuteBar(Guid.CreateVersion7(), At(12, 9, 15), 100m, 99m, 101m, 100m, 1, 1); // high below low
        await Should.ThrowAsync<PostgresException>(async () => await store.AppendAsync([bad], Ct));
    }

    [Fact]
    public async Task The_day_is_read_back_by_its_india_date_only()
    {
        var id = Guid.CreateVersion7();
        await WithAsync(p => p.GetRequiredService<IOpeningBarsStore>().AppendAsync([Bar(id, At(12, 9, 15)), Bar(id, At(9, 9, 15))], Ct).AsTask());

        var day = await WithAsync(p => p.GetRequiredService<IOpeningBarsQueries>().ReadDayAsync(new DateOnly(2026, 10, 12), Ct).AsTask());

        day.Select(b => b.MinuteStart).ShouldBe([At(12, 9, 15)]);
        day.Single().Volume.ShouldBe(500);
    }

    [Fact]
    public async Task The_history_takes_the_stored_minute_bars_first_and_a_live_row_only_for_a_minute_with_none_there_and_only_09_15_to_09_30()
    {
        var id = await SeedAsync("HIST");
        await using (var connection = await OpenAsync())
        {
            foreach (var (minute, open) in new[] { (At(8, 9, 15), 100m), (At(8, 9, 16), 100m), (At(8, 9, 30), 100m), (At(8, 14, 0), 100m) })
            {
                await ExecuteAsync(connection, "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (@id, @ts, @o, @o, @o, @o, 70, 'csv')", ("id", id), ("ts", minute.ToUniversalTime()), ("o", (double)open));
            }
        }

        await WithAsync(p => p.GetRequiredService<IOpeningBarsStore>().AppendAsync([Bar(id, At(8, 9, 16), 555m), Bar(id, At(8, 9, 17), 105m), Bar(id, At(9, 9, 15), 106m)], Ct).AsTask());

        var history = await WithAsync(p => p.GetRequiredService<IOpeningBarsQueries>().ReadHistoryAsync([id], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), 15, [], Ct).AsTask());

        history.OrderBy(b => b.MinuteStart).Select(b => (b.MinuteStart, b.Open)).ShouldBe([(At(8, 9, 15), 100m), (At(8, 9, 16), 100m), (At(8, 9, 17), 105m), (At(9, 9, 15), 106m)]);
        history.Single(b => b.MinuteStart == At(8, 9, 16)).Volume.ShouldBe(70, "the stored minute bar wins over the live row");
    }

    [Fact]
    public async Task The_history_leaves_out_live_rows_of_excluded_days_and_stops_at_the_requested_minutes()
    {
        var id = Guid.CreateVersion7();
        await WithAsync(p => p.GetRequiredService<IOpeningBarsStore>().AppendAsync([Bar(id, At(8, 9, 15)), Bar(id, At(8, 9, 22)), Bar(id, At(9, 9, 15)), Bar(id, At(9, 9, 20))], Ct).AsTask());

        var history = await WithAsync(p => p.GetRequiredService<IOpeningBarsQueries>().ReadHistoryAsync([id], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), 5, [new DateOnly(2026, 10, 8)], Ct).AsTask());

        history.Select(b => b.MinuteStart).ShouldBe([At(9, 9, 15)], "day 8 is excluded and 09:20 is past the five minutes");
    }

    [Fact]
    public async Task Events_come_back_in_order_with_an_open_gap_and_an_unknown_kind_is_refused()
    {
        var day = new DateOnly(2026, 10, 12);
        await WithAsync(async p =>
        {
            var store = p.GetRequiredService<IOpeningBarsStore>();
            await store.AddEventAsync(day, new OpeningBarsEvent(OpeningBarsEvent.Started, At(12, 9, 14)), Ct);
            await store.AddEventAsync(day, new OpeningBarsEvent(OpeningBarsEvent.Gap, At(12, 9, 17), At(12, 9, 18)), Ct);
            await store.AddEventAsync(day, new OpeningBarsEvent(OpeningBarsEvent.Gap, At(12, 9, 25)), Ct);
            return 0;
        });

        var events = await WithAsync(p => p.GetRequiredService<IOpeningBarsQueries>().ReadEventsAsync(day, Ct).AsTask());

        events.Select(e => e.Kind).ShouldBe([OpeningBarsEvent.Started, OpeningBarsEvent.Gap, OpeningBarsEvent.Gap]);
        events[1].To.ShouldBe(At(12, 9, 18));
        events[2].To.ShouldBeNull();
        await Should.ThrowAsync<PostgresException>(async () => await WithAsync(async p =>
        {
            await p.GetRequiredService<IOpeningBarsStore>().AddEventAsync(day, new OpeningBarsEvent("other", At(12, 9, 14)), Ct);
            return 0;
        }));
    }

    [Fact]
    public async Task The_owner_switch_is_off_with_no_row_newest_row_wins_and_only_an_owner_actor_may_write()
    {
        (await WithAsync(p => p.GetRequiredService<IOpeningBarsSwitchStore>().IsOnAsync(Ct).AsTask())).ShouldBeFalse();

        await WithAsync(async p =>
        {
            var store = p.GetRequiredService<IOpeningBarsSwitchStore>();
            await store.AppendAsync(true, "owner:test@example.invalid", "try it", Ct);
            return 0;
        });
        (await WithAsync(p => p.GetRequiredService<IOpeningBarsSwitchStore>().IsOnAsync(Ct).AsTask())).ShouldBeTrue();
        await WithAsync(async p =>
        {
            await p.GetRequiredService<IOpeningBarsSwitchStore>().AppendAsync(false, "owner:test@example.invalid", "stop", Ct);
            return 0;
        });

        (await WithAsync(p => p.GetRequiredService<IOpeningBarsSwitchStore>().IsOnAsync(Ct).AsTask())).ShouldBeFalse();
        await Should.ThrowAsync<PostgresException>(async () => await WithAsync(async p =>
        {
            await p.GetRequiredService<IOpeningBarsSwitchStore>().AppendAsync(true, "system:job", "no", Ct);
            return 0;
        }));
    }

    [Fact]
    public async Task A_frozen_not_formed_version_is_stored_with_its_formation_time_found_by_the_frozen_check_and_a_second_write_is_unchanged()
    {
        var friday = new DateOnly(2026, 10, 9);
        var basketId = Guid.CreateVersion7();
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "INSERT INTO market.baskets (id, name, colour, purpose, position, created_at, revision, kind, system) VALUES (@id, 'Derived: Gappers', '#64748b', 'watch', 0, now(), 0, 'derived', true)", ("id", basketId));
            await ExecuteAsync(connection, "INSERT INTO market.derived_definitions (basket_id, definition_no, enabled, cadence) VALUES (@id, 4, true, 'frozen_intraday')", ("id", basketId));
        }

        var definition = await WithAsync(async p => (await p.GetRequiredService<IDerivedVersionStore>().ListDefinitionsAsync(Ct)).Single(d => d.BasketId == basketId));
        (await WithAsync(p => p.GetRequiredService<IDerivedVersionStore>().FrozenExistsAsync(definition.BasketId, friday, Ct).AsTask())).ShouldBeFalse();
        var formed = At(12, 9, 20).ToUniversalTime();
        DerivedVersionWrite Version() => new(
            Guid.CreateVersion7(), definition.BasketId, definition.BasketName, friday, new DateOnly(2026, 10, 12), null, formed, "{}", "frozen-hash", 0, 0, 0,
            "[{\"InstrumentId\":\"00000000-0000-0000-0000-000000000000\",\"Symbol\":\"\",\"Reason\":\"feed_gap\"}]", "derived", "not_formed", [], formed, "system:opening-bars");

        var first = await WithAsync(p => p.GetRequiredService<IDerivedVersionStore>().WriteAsync(Version(), Ct).AsTask());
        var second = await WithAsync(p => p.GetRequiredService<IDerivedVersionStore>().WriteAsync(Version(), Ct).AsTask());

        first.Outcome.ShouldBe("applied");
        second.Outcome.ShouldBe("unchanged");
        (await WithAsync(p => p.GetRequiredService<IDerivedVersionStore>().FrozenExistsAsync(definition.BasketId, friday, Ct).AsTask())).ShouldBeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM market.basket_versions WHERE basket_id = @b AND outcome = 'not_formed' AND formed_at = @f", ("b", definition.BasketId), ("f", formed))).ShouldBe(1);
    }
}
