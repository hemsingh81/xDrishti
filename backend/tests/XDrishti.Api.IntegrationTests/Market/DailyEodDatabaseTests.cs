using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Market.Eod;
using XDrishti.Infrastructure.Market.Eod;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>CF-04 Part A, criteria 14 and 18 on a real TimescaleDB: <c>market.daily_eod</c>, its ledger and <c>market.eod_universe(asOf)</c>; the upsert is idempotent; the migration is reversible.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DailyEodDatabaseTests(ApiFactory factory)
{
    private const string Before = "20261102234713_AddFilingExtractions";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EodRow Row(string symbol, string isin, string day, decimal close = 100m, long traded = 1000) =>
        new(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), "EQ", symbol, isin, 99m, 101m, 98m, close, 99m, traded, traded * close, 5, null, null);

    private static async Task<long> CountAsync(NpgsqlDataSource source, string sql)
    {
        await using var command = source.CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task A_day_is_written_once_an_identical_rerun_changes_nothing_and_a_changed_row_is_updated()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_eod_store_probe", Ct);
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        var store = new EodStore(source);
        var day = new DateOnly(2024, 7, 5);
        var rows = new[] { Row("ABC", "INE000A01010", "2024-07-05") with { DeliveryQty = 600, DeliveryPct = 60.00m } };

        await store.WriteDayAsync(day, rows, new string('a', 64), new string('b', 64), Ct);
        var firstLoad = await CountAsync(source, "SELECT extract(epoch FROM loaded_at)::bigint * 1000000 + extract(microseconds FROM loaded_at)::bigint FROM market.daily_eod");
        await store.WriteDayAsync(day, rows, new string('a', 64), new string('b', 64), Ct);

        (await CountAsync(source, "SELECT count(*) FROM market.daily_eod")).ShouldBe(1);
        (await CountAsync(source, "SELECT count(*) FROM market.eod_files WHERE status = 'loaded'")).ShouldBe(2);
        (await CountAsync(source, "SELECT extract(epoch FROM loaded_at)::bigint * 1000000 + extract(microseconds FROM loaded_at)::bigint FROM market.daily_eod")).ShouldBe(firstLoad, "an identical row is not rewritten");
        (await store.ListDoneDatesAsync(day, day, Ct)).ShouldBe([day]);

        await store.WriteDayAsync(day, [Row("ABC", "INE000A01010", "2024-07-05", close: 105m)], new string('c', 64), null, Ct);
        (await CountAsync(source, "SELECT close::bigint FROM market.daily_eod")).ShouldBe(105);
    }

    [Fact]
    public async Task A_day_without_a_file_is_recorded_once_and_never_overwrites_a_loaded_day()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_eod_absent_probe", Ct);
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        var store = new EodStore(source);
        var holiday = new DateOnly(2024, 8, 15);
        var loaded = new DateOnly(2024, 7, 5);
        await store.WriteDayAsync(loaded, [Row("ABC", "INE000A01010", "2024-07-05")], new string('a', 64), null, Ct);

        await store.MarkAbsentAsync(holiday, Ct);
        await store.MarkAbsentAsync(holiday, Ct);
        await store.MarkAbsentAsync(loaded, Ct);

        (await store.ListDoneDatesAsync(loaded, holiday, Ct)).Order().ShouldBe([loaded, holiday]);
        (await CountAsync(source, "SELECT count(*) FROM market.eod_files WHERE status = 'absent'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_universe_as_of_a_date_lists_a_dead_name_before_its_last_day_and_not_after_and_never_adds_an_instrument()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_eod_universe_probe", Ct);
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        var store = new EodStore(source);
        await store.WriteDayAsync(new DateOnly(2021, 11, 1), [Row("DEAD", "INE111A01011", "2021-11-01"), Row("LIVE", "INE222A01012", "2021-11-01")], new string('a', 64), null, Ct);
        await store.WriteDayAsync(new DateOnly(2023, 1, 4), [Row("DEAD", "INE111A01011", "2023-01-04"), Row("LIVE", "INE222A01012", "2023-01-04")], new string('b', 64), null, Ct);
        await store.WriteDayAsync(new DateOnly(2023, 5, 31), [Row("LIVE", "INE222A01012", "2023-05-31")], new string('c', 64), null, Ct);
        await using (var insert = source.CreateCommand("""
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
            VALUES (gen_random_uuid(), 'NSE:LIVE', 'NSE', 'NSE_EQ', 'share', 'Live', 'Live', 'INE222A01012', 1, 'LIVE', false, now(), now())
            """))
        {
            await insert.ExecuteNonQueryAsync(Ct);
        }

        var instrumentsBefore = await CountAsync(source, "SELECT count(*) FROM market.instruments");
        var may2022 = await store.UniverseAsync(new DateOnly(2022, 5, 10), Ct);
        var june2023 = await store.UniverseAsync(new DateOnly(2023, 6, 1), Ct);
        var farAway = await store.UniverseAsync(new DateOnly(2023, 9, 1), Ct);

        may2022.Select(r => r.Symbol).Order().ShouldBe(["DEAD", "LIVE"]);
        may2022.Single(r => r.Symbol == "DEAD").ShouldSatisfyAllConditions(r => r.InCatalog.ShouldBeFalse(), r => r.KnownSince.ShouldBe(new DateOnly(2021, 11, 1)));
        may2022.Single(r => r.Symbol == "LIVE").InCatalog.ShouldBeTrue();
        june2023.Select(r => r.Symbol).ShouldBe(["LIVE"]);
        farAway.ShouldBeEmpty("an unloaded range answers empty, not stale");
        (await CountAsync(source, "SELECT count(*) FROM market.instruments")).ShouldBe(instrumentsBefore);
    }

    [Fact]
    public async Task Down_removes_the_tables_and_the_function_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_eod_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(source, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'market' AND table_name IN ('daily_eod', 'eod_files')")).ShouldBe(0);
        (await CountAsync(source, "SELECT count(*) FROM pg_proc WHERE proname = 'eod_universe'")).ShouldBe(0);

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(source, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'market' AND table_name IN ('daily_eod', 'eod_files')")).ShouldBe(2);
    }
}

/// <summary>CF-04 criterion 18: the runtime role reads, inserts and updates the daily tables and can never delete a row (on a throw-away server with the production role defaults).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DailyEodGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_can_insert_and_update_but_not_delete()
    {
        const string sha = "0000000000000000000000000000000000000000000000000000000000000000";
        await StoreKit.ExecuteAsync(server.App, $"INSERT INTO market.daily_eod (trade_date, series, symbol, open, high, low, close, traded_qty, turnover, source, file_sha256, loaded_at) VALUES ('2024-07-05', 'EQ', 'ABC', 1, 1, 1, 1, 1, 1, 'nse_cm', '{sha}', now())", Ct);
        await StoreKit.ExecuteAsync(server.App, "UPDATE market.daily_eod SET close = 2", Ct);
        await StoreKit.ExecuteAsync(server.App, $"INSERT INTO market.eod_files (trade_date, kind, status, row_count, fetched_at) VALUES ('2024-07-06', 'bhav', 'absent', 0, now())", Ct);

        foreach (var table in new[] { "market.daily_eod", "market.eod_files" })
        {
            var denied = await Should.ThrowAsync<PostgresException>(() => StoreKit.ExecuteAsync(server.App, $"DELETE FROM {table}", Ct));
            denied.SqlState.ShouldBe("42501", table);
        }
    }
}
