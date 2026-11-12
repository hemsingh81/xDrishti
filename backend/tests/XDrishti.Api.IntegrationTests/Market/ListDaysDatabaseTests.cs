using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Domain.Market.Baskets.Lists;
using XDrishti.Infrastructure.Market.Lists;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>DATA-01 (ADR 0274) criteria 3 to 5 on a real TimescaleDB: the dated rows are insert-only, an empty day is a covered day, an uncovered date answers not covered (never false), and the migration is reversible.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ListDaysDatabaseTests(ApiFactory factory)
{
    private const string Before = "20261112042137_AddSeamVerificationBasis";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> InstrumentAsync(NpgsqlDataSource source, string symbol)
    {
        var id = Guid.NewGuid();
        await using var insert = source.CreateCommand("""
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
            VALUES (@id, @canonical, 'NSE', 'NSE_EQ', 'share', @symbol, @symbol, NULL, 1, @symbol, false, now(), now())
            """);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("canonical", "NSE:" + symbol);
        insert.Parameters.AddWithValue("symbol", symbol);
        await insert.ExecuteNonQueryAsync(Ct);
        return id;
    }

    private static async Task<long> CountAsync(NpgsqlDataSource source, string sql)
    {
        await using var command = source.CreateCommand(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task A_day_is_stored_once_a_second_load_is_a_no_op_and_an_empty_day_is_covered_with_no_members()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_list_days_store_probe", Ct);
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        var store = new ListDayStore(source);
        var reader = new ListFlagsAsOfReader(source);
        var banned = await InstrumentAsync(source, "AAA");
        var other = await InstrumentAsync(source, "BBB");
        var monday = new DateOnly(2026, 11, 2);

        (await store.InsertAsync(ListBaskets.Ban.Id, monday, [banned], "archive", Ct)).ShouldBeTrue();
        (await store.InsertAsync(ListBaskets.Ban.Id, monday, [other], "archive", Ct)).ShouldBeFalse("a stored day is left as it is");
        (await store.InsertAsync(ListBaskets.Ban.Id, monday.AddDays(1), [], "archive", Ct)).ShouldBeTrue();

        (await CountAsync(source, "SELECT count(*) FROM market.list_day_members")).ShouldBe(1);
        (await store.CoveredAsync(ListBaskets.Ban.Id, monday, monday.AddDays(7), Ct)).Order().ShouldBe([monday, monday.AddDays(1)]);
        var ban = await reader.ForAsync([banned, other], monday, monday, Ct);
        ban.OnBan(banned).ShouldBe(true);
        ban.OnBan(other).ShouldBe(false);
        var empty = await reader.ForAsync([banned], monday.AddDays(1), monday, Ct);
        empty.OnBan(banned).ShouldBe(false, "a covered day with no ban");
    }

    [Fact]
    public async Task A_date_with_no_row_is_not_covered_and_answers_null_never_false()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_list_days_uncovered_probe", Ct);
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        var reader = new ListFlagsAsOfReader(source);
        var id = await InstrumentAsync(source, "AAA");

        var flags = await reader.ForAsync([id], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), Ct);

        flags.BanCovered.ShouldBeFalse();
        flags.SurveillanceCovered.ShouldBeFalse();
        flags.OnBan(id).ShouldBeNull();
        flags.UnderSurveillance(id).ShouldBeNull();
    }

    [Fact]
    public async Task The_forward_surveillance_list_answers_only_on_a_date_a_good_refresh_was_recorded_for_never_on_a_gap_day()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_list_days_forward_probe", Ct);
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        var reader = new ListFlagsAsOfReader(source);
        var flagged = await InstrumentAsync(source, "AAA");
        var clean = await InstrumentAsync(source, "BBB");
        await using (var seed = source.CreateCommand("""
            INSERT INTO market.basket_fetches (id, basket_id, fetched_at, rows, outcome, added, removed, unmatched, warnings, forced, actor)
            SELECT gen_random_uuid(), @asm, t, 1, 'applied', 0, 0, 0, '[]'::jsonb, false, 'system' FROM (VALUES ('2026-11-02 04:00:00+00'::timestamptz), ('2026-11-03 04:00:00+00'::timestamptz), ('2026-11-06 04:00:00+00'::timestamptz)) v(t);
            INSERT INTO market.basket_members (id, basket_id, instrument_id, valid_from, valid_to, position, added_by, removed_by)
            VALUES (gen_random_uuid(), @asm, @flagged, '2026-11-02 04:00:00+00', '2026-11-03 04:00:00+00', 0, 'system', 'system');
            """))
        {
            seed.Parameters.AddWithValue("asm", ListBaskets.AsmGsm.Id);
            seed.Parameters.AddWithValue("flagged", flagged);
            await seed.ExecuteNonQueryAsync(Ct);
        }

        var first = await reader.ForAsync([flagged, clean], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2), Ct);
        var boundary = await reader.ForAsync([flagged, clean], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3), Ct);
        var gap = await reader.ForAsync([flagged, clean], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4), Ct);
        var after = await reader.ForAsync([flagged, clean], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), Ct);

        first.UnderSurveillance(flagged).ShouldBe(true);
        first.UnderSurveillance(clean).ShouldBe(false);
        boundary.UnderSurveillance(flagged).ShouldBe(false, "the stay ended on 11-03: valid_to is exclusive");
        gap.SurveillanceCovered.ShouldBeFalse("no refresh was recorded for 11-04");
        gap.UnderSurveillance(flagged).ShouldBeNull();
        after.UnderSurveillance(flagged).ShouldBeNull();
    }

    [Fact]
    public async Task A_ban_on_the_first_day_and_none_the_next_reads_true_then_false_and_a_gap_day_reads_unavailable()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_list_days_boundary_probe", Ct);
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        var store = new ListDayStore(source);
        var reader = new ListFlagsAsOfReader(source);
        var id = await InstrumentAsync(source, "AAA");
        var d = new DateOnly(2026, 11, 2);
        await store.InsertAsync(ListBaskets.Ban.Id, d, [id], "daily", Ct);
        await store.InsertAsync(ListBaskets.Ban.Id, d.AddDays(1), [], "daily", Ct);

        (await reader.ForAsync([id], d, d, Ct)).OnBan(id).ShouldBe(true);
        (await reader.ForAsync([id], d.AddDays(1), d, Ct)).OnBan(id).ShouldBe(false);
        (await reader.ForAsync([id], d.AddDays(2), d, Ct)).OnBan(id).ShouldBeNull("no file was stored for that date, so it is not 'not banned'");
    }

    [Fact]
    public async Task Down_removes_the_tables_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_list_days_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var source = NpgsqlDataSource.Create(scratch.ConnectionString);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(source, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'market' AND table_name IN ('list_day_coverage', 'list_day_members')")).ShouldBe(0);

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(source, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'market' AND table_name IN ('list_day_coverage', 'list_day_members')")).ShouldBe(2);
    }
}

/// <summary>DATA-01 criterion 4: the runtime role reads and inserts the dated list rows and can neither update nor delete one.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ListDaysGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_can_insert_but_neither_update_nor_delete()
    {
        await StoreKit.ExecuteAsync(server.App, $"INSERT INTO market.list_day_coverage (basket_id, trade_date, source, member_count, loaded_at) VALUES ('{ListBaskets.Ban.Id}', '2026-11-02', 'archive', 0, now())", Ct);

        foreach (var sql in new[] { "UPDATE market.list_day_coverage SET member_count = 1", "DELETE FROM market.list_day_coverage", "UPDATE market.list_day_members SET trade_date = trade_date", "DELETE FROM market.list_day_members" })
        {
            var denied = await Should.ThrowAsync<PostgresException>(() => StoreKit.ExecuteAsync(server.App, sql, Ct));
            denied.SqlState.ShouldBe("42501", sql);
        }
    }
}
