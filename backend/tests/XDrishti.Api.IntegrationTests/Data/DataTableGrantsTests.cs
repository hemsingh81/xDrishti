using System.Net;
using System.Net.Http.Json;
using Npgsql;
using XDrishti.Api.Auth;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Data;

/// <summary>
/// The bars work with the production runtime role xd_app (DML only, no DELETE on bars and aggregates, no DDL): the test creates the role, recreates the production
/// default privileges (deploy/db/init/10-roles.sh) in a scratch database, runs the real migrations there as the owner (extension, hypertable, compression, policy),
/// then runs the whole pipeline through the API connected as xd_app — also into chunks that were compressed meanwhile.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DataTableGrantsTests(ApiFactory factory)
{
    private const string ScratchDatabase = "xd_data_grants_probe";

    private const string AppPassword = "app-role-probe-only";

    private static readonly string[] Tables = ["bars_1m", "bars_agg", "bar_coverage", "csv_files", "data_jobs", "bar_gaps", "quality_runs", "quality_results", "corporate_actions"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_runs_the_whole_pipeline_on_the_hypertable_including_compressed_chunks_but_cannot_delete_or_change_tables()
    {
        await WithMigratedScratchAsync(async (owner, scratch) =>
        {
            await CheckHypertableAsync(owner);

            await using (var connection = new NpgsqlConnection(scratch.ConnectionString))
            {
                await connection.OpenAsync(Ct);
                await ExecuteAsync(connection, "SET ROLE xd_app");
                await CheckPrivilegesAsync(connection); // ends as the owner
                await ExecuteAsync(connection, "SET ROLE xd_app");
                await ExecuteAsync(connection, "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (gen_random_uuid(), '2026-03-02 04:00+00', 1, 2, 1, 2, 3, 'csv')"); // a chunk created by xd_app after the migrations
                await CheckChunkPrivilegesAsync(connection, "an ordinary chunk", compressed: false, expectedAtLeast: 1);
                await ExecuteAsync(connection, "RESET ROLE");
                await ExecuteAsync(connection, "DELETE FROM market.bars_1m");
            }

            await RunPipelineAsAppRoleAsync(owner, new NpgsqlConnectionStringBuilder(scratch.ConnectionString) { Username = "xd_app", Password = AppPassword });
        });
    }

    /// <summary>
    /// Creates the scratch database with the production default privileges, runs the real migrations in it as the owner and hands the owner's connection (and the database's connection string) to the test; always cleans up.
    /// </summary>
    private async Task WithMigratedScratchAsync(Func<NpgsqlConnection, NpgsqlConnectionStringBuilder, Task> body)
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = ScratchDatabase, Pooling = false };
        await CreateScratchDatabaseAsync(admin, scratch);
        factory.Dhan.Reset();
        try
        {
            await using (var migrated = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString)))
            {
                await DatabaseMigrator.MigrateAsync(migrated.Services, Ct);
            }

            await using var owner = new NpgsqlConnection(scratch.ConnectionString);
            await owner.OpenAsync(Ct);
            await body(owner, scratch);
        }
        finally
        {
            factory.Dhan.Reset();
            foreach (var file in Directory.EnumerateFileSystemEntries(factory.CsvFolder))
            {
                File.Delete(file);
            }

            await DropScratchDatabaseAsync(admin);
        }
    }

    /// <summary>
    /// <c>market.undo_csv_bars</c> is the only way for the runtime role to remove bars (ADR 0017 §6), and it is narrow: as <c>xd_app</c>, with the real extension, on compressed chunks, it deletes only <c>csv</c> rows of the one
    /// instrument inside the range (ends included) and the derived buckets of that range, keeps the coverage true, ignores a hostile search path, and cannot be run by another role or redefined by <c>xd_app</c>.
    /// </summary>
    [Fact]
    public async Task The_undo_function_lets_the_app_role_remove_only_csv_bars_of_one_instrument_in_a_range_and_nothing_else()
    {
        await WithMigratedScratchAsync(async (owner, scratch) =>
        {
            var asApp = new NpgsqlConnectionStringBuilder(scratch.ConnectionString) { Username = "xd_app", Password = AppPassword };
            await ExecuteAsync(owner, "CREATE ROLE xd_probe_other NOLOGIN");
            try
            {
                await ExecuteAsync(owner, "GRANT USAGE ON SCHEMA market TO xd_probe_other");
                await CheckUndoFunctionAsync(owner, asApp);
            }
            finally
            {
                await ExecuteAsync(owner, "DROP OWNED BY xd_probe_other");
                await ExecuteAsync(owner, "DROP ROLE xd_probe_other");
            }
        });
    }

    private static async Task InsertBarsAsync(NpgsqlConnection owner, Guid instrument, string source, string from, string to) =>
        await ExecuteAsync(owner, $"INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) SELECT '{instrument}', g, 10, 12, 9, 11, 100, '{source}' FROM generate_series('{from}'::timestamptz, '{to}'::timestamptz, interval '1 minute') g");

    private static async Task InsertBucketAsync(NpgsqlConnection owner, Guid instrument, string timeframe, string ts) =>
        await ExecuteAsync(owner, $"INSERT INTO market.bars_agg (instrument_id, timeframe, ts, open, high, low, close, volume, bars, complete) VALUES ('{instrument}', '{timeframe}', '{ts}', 10, 12, 9, 11, 100, 5, false)");

    private static async Task InsertCoverageAsync(NpgsqlConnection owner, Guid instrument) =>
        await ExecuteAsync(owner, $"INSERT INTO market.bar_coverage (instrument_id, first_bar, last_bar, bars, csv_bars, dhan_bars, updated_at, aggregates) SELECT instrument_id, min(ts), max(ts), count(*), count(*) FILTER (WHERE source = 'csv'), count(*) FILTER (WHERE source = 'dhan'), now(), '{{}}' FROM market.bars_1m WHERE instrument_id = '{instrument}' GROUP BY instrument_id");

    private static async Task<string> StateAsync(NpgsqlConnection owner, Guid instrument) => (string)(await ScalarAsync(
        owner,
        $"""
        SELECT (SELECT count(*) FROM market.bars_1m WHERE instrument_id = '{instrument}' AND source = 'csv') || '/' || (SELECT count(*) FROM market.bars_1m WHERE instrument_id = '{instrument}' AND source = 'dhan')
            || '/' || (SELECT count(*) FROM market.bars_agg WHERE instrument_id = '{instrument}')
        """))!;

    private static async Task CheckUndoFunctionAsync(NpgsqlConnection owner, NpgsqlConnectionStringBuilder asApp)
    {
        // The function: security definer, owned by the migrating (owner) role, search path pinned with the system schema first and temporary objects last.
        (await ScalarAsync(owner, "SELECT prosecdef FROM pg_proc WHERE oid = 'market.undo_csv_bars(uuid, timestamptz, timestamptz)'::regprocedure")).ShouldBe(true);
        (await ScalarAsync(owner, "SELECT array_to_string(proconfig, ';') FROM pg_proc WHERE oid = 'market.undo_csv_bars(uuid, timestamptz, timestamptz)'::regprocedure")).ShouldBe("search_path=pg_catalog, market, pg_temp");
        (await ScalarAsync(owner, "SELECT pg_get_userbyid(proowner) <> 'xd_app' FROM pg_proc WHERE oid = 'market.undo_csv_bars(uuid, timestamptz, timestamptz)'::regprocedure")).ShouldBe(true);
        (await ScalarAsync(owner, "SELECT has_function_privilege('xd_app', 'market.undo_csv_bars(uuid, timestamptz, timestamptz)', 'EXECUTE')")).ShouldBe(true);
        (await ScalarAsync(owner, "SELECT has_function_privilege('xd_probe_other', 'market.undo_csv_bars(uuid, timestamptz, timestamptz)', 'EXECUTE')")).ShouldBe(false);
        (await ScalarAsync(owner, "SELECT count(*) FROM pg_proc p, aclexplode(p.proacl) a WHERE p.oid = 'market.undo_csv_bars(uuid, timestamptz, timestamptz)'::regprocedure AND a.grantee = 0")).ShouldBe(0L); // nothing granted to PUBLIC

        // Two instruments with CSV bars on three days (60 minutes from 09:15 India each) and, for the first, ten minutes of the second day that Dhan took over.
        var a = await SeedPlainAsync(owner, "AAA");
        var b = await SeedPlainAsync(owner, "BBB");
        foreach (var instrument in new[] { a, b })
        {
            await InsertBarsAsync(owner, instrument, "csv", "2026-01-05 03:45+00", "2026-01-05 04:44+00");
            await InsertBarsAsync(owner, instrument, "csv", "2026-01-06 03:45+00", "2026-01-06 04:44+00");
            await InsertBarsAsync(owner, instrument, "csv", "2026-01-07 03:45+00", "2026-01-07 04:44+00");
        }

        await ExecuteAsync(owner, $"UPDATE market.bars_1m SET source = 'dhan' WHERE instrument_id = '{a}' AND ts BETWEEN '2026-01-06 04:00+00' AND '2026-01-06 04:09+00'");
        foreach (var (timeframe, ts) in new[] { ("1D", "2026-01-04 18:30+00"), ("1D", "2026-01-05 18:30+00"), ("1D", "2026-01-06 18:30+00"), ("1D", "2026-01-11 18:30+00"), ("5m", "2026-01-05 03:45+00"), ("5m", "2026-01-07 03:45+00"),
                                                ("1W", "2026-01-04 18:30+00"), ("1M", "2025-12-31 18:30+00"), ("1M", "2025-11-30 18:30+00") })
        {
            await InsertBucketAsync(owner, a, timeframe, ts);
        }

        await InsertBucketAsync(owner, b, "1D", "2026-01-04 18:30+00");
        await InsertCoverageAsync(owner, a);
        await InsertCoverageAsync(owner, b);
        (await StateAsync(owner, a)).ShouldBe("170/10/9");
        ((long)(await ScalarAsync(owner, "SELECT count(compress_chunk(c)) FROM show_chunks('market.bars_1m') c"))!).ShouldBeGreaterThan(0L); // everything below happens on compressed chunks

        await using var connection = new NpgsqlConnection(asApp.ConnectionString);
        await connection.OpenAsync(Ct);

        // The runtime role still has no way to delete bars itself, nor to redefine or re-own the function.
        foreach (var table in new[] { "bars_1m", "bars_agg", "bar_coverage", "csv_files" })
        {
            (await ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'market.{table}', 'DELETE')")).ShouldBe(false, table);
        }

        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DELETE FROM market.bars_1m WHERE source = 'csv'"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "TRUNCATE market.bars_1m"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "CREATE OR REPLACE FUNCTION market.undo_csv_bars(p_instrument uuid, p_from timestamptz, p_to timestamptz) RETURNS bigint LANGUAGE sql AS 'SELECT 0::bigint'"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "ALTER FUNCTION market.undo_csv_bars(uuid, timestamptz, timestamptz) SET search_path = public"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DROP FUNCTION market.undo_csv_bars(uuid, timestamptz, timestamptz)"), PostgresErrorCodes.InsufficientPrivilege);
        await ExecuteAsync(connection, "GRANT EXECUTE ON FUNCTION market.undo_csv_bars(uuid, timestamptz, timestamptz) TO PUBLIC"); // without the grant option this only warns: it grants nothing
        (await ScalarAsync(owner, "SELECT has_function_privilege('xd_probe_other', 'market.undo_csv_bars(uuid, timestamptz, timestamptz)', 'EXECUTE')")).ShouldBe(false);

        // No other role can run it.
        await ExecuteAsync(owner, "SET ROLE xd_probe_other");
        await ShouldBeRejectedAsync(owner, () => ExecuteAsync(owner, $"SELECT market.undo_csv_bars('{a}', '2026-01-05 03:45+00', '2026-01-07 04:44+00')"), PostgresErrorCodes.InsufficientPrivilege);
        await ExecuteAsync(owner, "RESET ROLE");

        // A wrong instrument or a range with no CSV bars of the instrument deletes nothing (and does not touch the coverage).
        (await ScalarAsync(connection, $"SELECT market.undo_csv_bars('{Guid.CreateVersion7()}', '2026-01-05 03:45+00', '2026-01-07 04:44+00')")).ShouldBe(0L);
        (await ScalarAsync(connection, $"SELECT market.undo_csv_bars('{a}', '2027-01-05 03:45+00', '2027-01-07 04:44+00')")).ShouldBe(0L);
        (await ScalarAsync(connection, $"SELECT market.undo_csv_bars('{a}', '2026-01-06 04:00+00', '2026-01-06 04:09+00')")).ShouldBe(0L); // only Dhan bars there
        (await StateAsync(owner, a)).ShouldBe("170/10/9");
        (await StateAsync(owner, b)).ShouldBe("180/0/1");
        (await ScalarAsync(owner, $"SELECT dirty_from IS NULL AND dirty_to IS NULL FROM market.bar_coverage WHERE instrument_id = '{a}'")).ShouldBe(true);

        // A hostile search path: decoys in a schema the caller controls, an operator that is always true, a function that would fail - none is picked up, and a temp table of the same name is not touched.
        await ExecuteAsync(owner, "CREATE SCHEMA evil; GRANT USAGE, CREATE ON SCHEMA evil TO xd_app");
        await ExecuteAsync(connection, "CREATE TABLE evil.bars_1m (instrument_id uuid, ts timestamptz, source text)");
        await ExecuteAsync(connection, "CREATE TABLE evil.bars_agg (instrument_id uuid, timeframe text, ts timestamptz)");
        await ExecuteAsync(connection, "CREATE TABLE evil.bar_coverage (instrument_id uuid, dirty_from timestamptz, dirty_to timestamptz)");
        await ExecuteAsync(connection, $"INSERT INTO evil.bars_1m VALUES ('{a}', '2026-01-05 04:00+00', 'csv')");
        await ExecuteAsync(connection, "CREATE FUNCTION evil.date_bin(interval, timestamptz, timestamptz) RETURNS timestamptz LANGUAGE plpgsql AS $f$ BEGIN RAISE EXCEPTION 'injected date_bin'; END $f$");
        await ExecuteAsync(connection, "CREATE FUNCTION evil.now() RETURNS timestamptz LANGUAGE plpgsql AS $f$ BEGIN RAISE EXCEPTION 'injected now'; END $f$");
        await ExecuteAsync(connection, "CREATE FUNCTION evil.always(timestamptz, timestamptz) RETURNS boolean LANGUAGE sql AS 'SELECT true'");
        await ExecuteAsync(connection, "CREATE OPERATOR evil.>= (LEFTARG = timestamptz, RIGHTARG = timestamptz, FUNCTION = evil.always)");
        await ExecuteAsync(connection, "CREATE TEMP TABLE bars_1m (instrument_id uuid, ts timestamptz, source text)");
        await ExecuteAsync(connection, $"INSERT INTO pg_temp.bars_1m VALUES ('{a}', '2026-01-05 04:00+00', 'csv')");
        await ExecuteAsync(connection, "SET search_path = evil, public, pg_temp, market");
        (await ScalarAsync(connection, $"SELECT market.undo_csv_bars('{a}', '2026-01-05 04:00+00', '2026-01-05 04:04+00')")).ShouldBe(5L); // both ends included: 04:00 .. 04:04
        await ExecuteAsync(connection, "RESET search_path");
        (await ScalarAsync(connection, "SELECT count(*) FROM evil.bars_1m")).ShouldBe(1L);
        (await ScalarAsync(connection, "SELECT count(*) FROM pg_temp.bars_1m")).ShouldBe(1L);
        (await StateAsync(owner, a)).ShouldBe("165/10/5"); // 170 - 5 csv bars; the buckets of that day, its week and its month went (1D, 5m, 1W, 1M) but not the other days' ones
        (await ScalarAsync(owner, $"SELECT count(*) FROM market.bars_1m WHERE instrument_id = '{a}' AND ts IN ('2026-01-05 03:59+00', '2026-01-05 04:05+00')")).ShouldBe(2L); // the neighbours of the range stay
        (await StateAsync(owner, b)).ShouldBe("180/0/1");

        // The whole span of the file for A: the CSV bars of days 1 and 2 go, the 10 Dhan bars and the CSV bars of day 3 stay, the buckets of the touched days, week and month go.
        (await ScalarAsync(connection, $"SELECT market.undo_csv_bars('{a}', '2026-01-05 03:45+00', '2026-01-06 04:44+00')")).ShouldBe(105L); // 60 + 60 - 10 dhan - 5 already removed
        (await StateAsync(owner, a)).ShouldBe("60/10/4");
        (await ScalarAsync(owner, $"SELECT string_agg(timeframe || '@' || to_char(ts AT TIME ZONE 'UTC', 'MM-DD'), ',' ORDER BY timeframe, ts) FROM market.bars_agg WHERE instrument_id = '{a}'")).ShouldBe("1D@01-06,1D@01-11,1M@11-30,5m@01-07"); // the day bar of 7 Jan (UTC 01-06 18:30), of 12 Jan, December's month bar and 7 Jan's 5-minute bar stay
        (await ScalarAsync(owner, $"SELECT bars || '/' || csv_bars || '/' || dhan_bars || '/' || to_char(first_bar AT TIME ZONE 'UTC', 'MM-DD HH24:MI') || '/' || to_char(last_bar AT TIME ZONE 'UTC', 'MM-DD HH24:MI') || '/' || to_char(dirty_from AT TIME ZONE 'UTC', 'MM-DD HH24:MI') || '/' || to_char(dirty_to AT TIME ZONE 'UTC', 'MM-DD HH24:MI') FROM market.bar_coverage WHERE instrument_id = '{a}'"))
            .ShouldBe("70/60/10/01-06 04:00/01-07 04:44/01-05 03:45/01-06 04:44");

        // Another instrument's bars were never part of it; asked for, all its CSV bars go, and with no bar left its coverage row goes too.
        (await ScalarAsync(connection, $"SELECT market.undo_csv_bars('{b}', '2026-01-05 03:45+00', '2026-01-07 04:44+00')")).ShouldBe(180L);
        (await StateAsync(owner, b)).ShouldBe("0/0/0");
        (await ScalarAsync(owner, $"SELECT count(*) FROM market.bar_coverage WHERE instrument_id = '{b}'")).ShouldBe(0L);
        (await StateAsync(owner, a)).ShouldBe("60/10/4");

        // The compressed chunks took the deletes by decompressing the batches they touched: they are still compressed, now partially (status bits 1 and 8) - TimescaleDB's compression policy, or compress_chunk, packs them again - and every row is still there.
        const string ChunkStatus = "SELECT string_agg(c.status::text, ',' ORDER BY c.id) FROM _timescaledb_catalog.chunk c WHERE c.hypertable_id = (SELECT id FROM _timescaledb_catalog.hypertable WHERE table_name = 'bars_1m')";
        (await ScalarAsync(owner, ChunkStatus)).ShouldBe("9,9");
        (await ScalarAsync(connection, $"SELECT count(*) FROM market.bars_1m WHERE instrument_id = '{a}'")).ShouldBe(70L);
        ((long)(await ScalarAsync(owner, "SELECT count(compress_chunk(c, if_not_compressed => true)) FROM show_chunks('market.bars_1m') c"))!).ShouldBe(2L);
        (await ScalarAsync(owner, ChunkStatus)).ShouldBe("1,1");
        (await ScalarAsync(connection, $"SELECT count(*) FROM market.bars_1m WHERE instrument_id = '{a}'")).ShouldBe(70L);
    }

    private static async Task CheckHypertableAsync(NpgsqlConnection owner)
    {
        (await ScalarAsync(owner, "SELECT count(*) FROM timescaledb_information.hypertables WHERE hypertable_schema = 'market' AND hypertable_name = 'bars_1m' AND compression_enabled")).ShouldBe(1L);
        (await ScalarAsync(owner, "SELECT time_interval::text FROM timescaledb_information.dimensions WHERE hypertable_schema = 'market' AND hypertable_name = 'bars_1m'")).ShouldBe("30 days");
        (await ScalarAsync(owner, "SELECT config ->> 'compress_after' FROM timescaledb_information.jobs WHERE proc_name = 'policy_compression' AND hypertable_name = 'bars_1m'")).ShouldBe("7 days");
        (await ScalarAsync(owner, "SELECT segmentby_column_index FROM timescaledb_information.compression_settings WHERE hypertable_name = 'bars_1m' AND attname = 'instrument_id'")).ShouldBe(1);
        (await ScalarAsync(owner, "SELECT orderby_column_index FROM timescaledb_information.compression_settings WHERE hypertable_name = 'bars_1m' AND attname = 'ts'")).ShouldBe(1);
        (await ScalarAsync(owner, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'market' AND tablename = 'bars_1m'")).ShouldBe(1L); // the primary key only
        (await ScalarAsync(owner, "SELECT count(*) FROM timescaledb_information.hypertables WHERE hypertable_schema = 'market' AND hypertable_name = 'bars_agg'")).ShouldBe(0L);
    }

    private static async Task CheckPrivilegesAsync(NpgsqlConnection connection)
    {
        foreach (var table in Tables)
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                (await ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'market.{table}', '{privilege}')")).ShouldBe(privilege != "DELETE", $"{privilege} on {table}");
            }
        }

        // The trading calendar is small configuration the owner edits: plain DML, deletes included (and still no DDL).
        foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
        {
            (await ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'market.trading_days', '{privilege}')")).ShouldBe(true, $"{privilege} on trading_days");
        }

        await ExecuteAsync(connection, "INSERT INTO market.trading_days (date, kind, source, status, updated_at) VALUES ('2026-10-02', 'holiday', 'manual', 'confirmed', now())");
        await ExecuteAsync(connection, "DELETE FROM market.trading_days WHERE date = '2026-10-02'");
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "INSERT INTO market.trading_days (date, kind, source, status, updated_at) VALUES ('2026-10-02', 'special', 'manual', 'confirmed', now())"), PostgresErrorCodes.CheckViolation); // a special session needs a window
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "ALTER TABLE market.trading_days ADD COLUMN x int"), PostgresErrorCodes.InsufficientPrivilege);
        await ExecuteAsync(connection, "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (gen_random_uuid(), '2026-01-05 04:00+00', 1, 2, 1, 2, 3, 'csv')");
        await ExecuteAsync(connection, "UPDATE market.bars_1m SET volume = 4");
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DELETE FROM market.bars_1m"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DELETE FROM market.bars_agg"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "TRUNCATE market.bars_1m"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "ALTER TABLE market.bars_1m ADD COLUMN x int"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DROP TABLE market.bars_agg"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "SELECT compress_chunk(c) FROM show_chunks('market.bars_1m') c"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (gen_random_uuid(), '2026-01-05 04:00+00', 1, 2, 3, 2, 3, 'csv')"), PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source) VALUES (gen_random_uuid(), '2026-01-05 04:00+00', 1, 2, 1, 2, 3, 'ftp')"), PostgresErrorCodes.CheckViolation);

        // Corporate actions: the runtime role records suggestions and decides on them (insert, update) but never deletes; the table's own rules hold for it too.
        await ExecuteAsync(connection, """
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
            VALUES ('00000000-0000-7000-8000-00000000c001', 'NSE:PROBECA', 'NSE', 'NSE_EQ', 'share', 'PROBECA', 'Probe', 1, 'PROBECA', false, now(), now())
            """);
        const string Insert = "INSERT INTO market.corporate_actions (id, instrument_id, ex_date, kind, numerator, denominator, factor, status, source, created_at) VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000c001', '2026-09-01', 'split', 1, 5, 0.2, 'suggested', 'detected', now())";
        await ExecuteAsync(connection, Insert);
        await ExecuteAsync(connection, "UPDATE market.corporate_actions SET status = 'applied', decided_at = now(), decided_by = 'probe'");
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, Insert), PostgresErrorCodes.UniqueViolation); // one per instrument, ex-date and kind
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "UPDATE market.corporate_actions SET factor = 0"), PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "UPDATE market.corporate_actions SET status = 'suggested'"), PostgresErrorCodes.CheckViolation); // a suggestion has no decision
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "UPDATE market.corporate_actions SET source = 'ftp'"), PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DELETE FROM market.corporate_actions"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "ALTER TABLE market.corporate_actions ADD COLUMN x int"), PostgresErrorCodes.InsufficientPrivilege);
        await ExecuteAsync(connection, "RESET ROLE");
        await ExecuteAsync(connection, "DELETE FROM market.corporate_actions; DELETE FROM market.instruments WHERE canonical_symbol = 'NSE:PROBECA'"); // the owner tidies up
        await ExecuteAsync(connection, "DELETE FROM market.bars_1m"); // the owner may (and tidies up the probe row)
    }

    private async Task RunPipelineAsAppRoleAsync(NpgsqlConnection owner, NpgsqlConnectionStringBuilder asApp)
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", asApp.ConnectionString));
        (await OwnerBootstrap.ProvisionAsync(host.Services)).ShouldBe(0);
        var client = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.18.250.99");
        using var login = await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
        login.EnsureSuccessStatusCode();
        var session = (await login.Content.ReadFromJsonAsync<XDrishti.Application.Auth.SessionInfo>(Ct))!;
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={ApiFactory.SessionCookieValue(login)}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);

        var abb = await SeedAsync(owner, "ABB");
        var older = BarFixtures.Session(new DateOnly(2026, 1, 5), 1);
        BarFixtures.WriteFile(factory.CsvFolder, "ABB", BarFixtures.Csv([.. older, .. BarFixtures.Session(new DateOnly(2026, 1, 6), 1)]));

        // Round 1: CSV, a Dhan fetch (a fake source, as the Data account) and the aggregates, all as xd_app.
        var recent = BarFixtures.Session(new DateOnly(2026, 4, 30), 4);
        factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, recent, 1));
        factory.Dhan.UseConnectedAccount();
        var first = await RunJobAsync(client, host.Services);
        first.Status.ShouldBe("succeeded", first.Error);
        (first.Csv.Inserted, first.Dhan.Inserted, first.Aggregate.Done).ShouldBe((750, 375, 1));
        (await ScalarAsync(owner, "SELECT count(*) FROM market.bars_1m")).ShouldBe(1125L);
        (await ScalarAsync(owner, "SELECT count(*) FROM market.bars_agg WHERE timeframe = '1D'")).ShouldBe(3L);

        // The compression policy would compress these old chunks after a week: do it now (as the owner), then change data in them as xd_app.
        ((long)(await ScalarAsync(owner, "SELECT count(compress_chunk(c)) FROM show_chunks('market.bars_1m') c"))!).ShouldBeGreaterThan(0L);
        ((long)(await ScalarAsync(owner, "SELECT count(*) FROM timescaledb_information.chunks WHERE hypertable_name = 'bars_1m' AND is_compressed"))!).ShouldBeGreaterThan(0L);
        await using (var runtime = new NpgsqlConnection(asApp.ConnectionString))
        {
            await runtime.OpenAsync(Ct);
            await CheckChunkPrivilegesAsync(runtime, "a compressed chunk", compressed: true, expectedAtLeast: 1);
            await CheckChunkPrivilegesAsync(runtime, "an ordinary chunk", compressed: false, expectedAtLeast: 1);
        }

        // Round 2: a revised CSV file (changed minutes inside compressed chunks and new minutes next to them) and a Dhan overlay that replaces CSV bars there.
        var revised = BarFixtures.Session(new DateOnly(2026, 1, 5), 1);
        revised[10] = revised[10] with { Close = revised[10].High, Volume = 99 };
        var extra = BarFixtures.Session(new DateOnly(2026, 1, 7), 1);
        BarFixtures.WriteFile(factory.CsvFolder, "ABB", BarFixtures.Csv([.. revised, .. BarFixtures.Session(new DateOnly(2026, 1, 6), 1), .. extra]), modified: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
        var overlay = BarFixtures.Session(new DateOnly(2026, 1, 6), 1).Take(5).Select(b => b with { Close = b.High, Volume = b.Volume + 7 }).ToList();
        factory.Dhan.UseSource(t => new IntradayResult(t.InstrumentId, overlay, 1));
        var second = await RunJobAsync(client, host.Services);
        second.Status.ShouldBe("succeeded", second.Error);
        (second.Csv.Inserted, second.Csv.Updated, second.Dhan.Updated).ShouldBe((375, 1, 5));
        (await ScalarAsync(owner, "SELECT count(*) FROM market.bars_1m")).ShouldBe(1500L);
        (await ScalarAsync(owner, $"SELECT volume FROM market.bars_1m WHERE instrument_id = '{abb}' AND ts = to_timestamp({revised[10].EpochSeconds})")).ShouldBe(99L);
        (await ScalarAsync(owner, "SELECT count(*) FROM market.bars_1m WHERE source = 'dhan'")).ShouldBe(380L);
        (await ScalarAsync(owner, "SELECT count(*) FROM market.bars_agg WHERE timeframe = '1D'")).ShouldBe(4L);

        // A full rebuild as xd_app reads the compressed chunks and finds nothing to change.
        var rebuilt = await RunJobAsync(client, host.Services, ["aggregate"]);
        (rebuilt.Aggregate.Full, rebuilt.Aggregate.Buckets).ShouldBe((true, 0));

        // The data quality checks write their run and results as xd_app (insert and update, never delete) and the read API reads them.
        var qualityJob = await RunJobAsync(client, host.Services, ["quality"]);
        qualityJob.Status.ShouldBe("succeeded", qualityJob.Error);
        (qualityJob.Quality!.Status, qualityJob.Quality.Done).ShouldBe(("succeeded", 1));
        (await ScalarAsync(owner, "SELECT count(*) FROM market.quality_results")).ShouldBe(1L);
        (await ScalarAsync(owner, "SELECT status FROM market.quality_runs")).ShouldBe("succeeded");
        using var qualityOverview = await client.GetAsync("/api/data/quality/overview", Ct);
        (await qualityOverview.ReadAsync<XDrishti.Application.Market.Quality.QualityOverview>(Ct)).Counts.Checked.ShouldBe(1);

        // The jobs above ended with the calendar upkeep as xd_app: the single instrument's gap from January to April is proposed (observed, never confirmed). The owner's
        // add, confirm and remove work as xd_app too.
        ((long)(await ScalarAsync(owner, "SELECT count(*) FROM market.trading_days WHERE source = 'observed' AND status = 'proposed'"))!).ShouldBeGreaterThan(0L);
        using var added = await client.PostJsonAsync("/api/data/calendar", new { date = "2026-11-09", kind = "holiday" }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var confirmed = await client.PutJsonAsync("/api/data/calendar/2026-02-02/confirm", new { }, Ct);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var removed = await client.DeleteAsync("/api/data/calendar/2026-11-09", Ct);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ScalarAsync(owner, "SELECT count(*) FROM market.trading_days WHERE date = '2026-11-09'")).ShouldBe(0L);
        (await ScalarAsync(owner, "SELECT status FROM market.trading_days WHERE date = '2026-02-02'")).ShouldBe("confirmed");

        // Corporate actions as xd_app: add, edit, ignore, the detection (ON CONFLICT DO NOTHING inserts) and a CSV import, each with its audit event in accounts.account_events.
        using var action = await client.PostJsonAsync("/api/data/corporate-actions", new { instrumentId = abb, exDate = "2026-02-01", kind = "split", numerator = 1, denominator = 5 }, Ct);
        action.StatusCode.ShouldBe(HttpStatusCode.Created, await action.Content.ReadAsStringAsync(Ct));
        var actionId = (await action.Content.ReadFromJsonAsync<XDrishti.Application.Market.Actions.CorporateActionView>(Ct))!.Id;
        using var edited = await client.PutJsonAsync($"/api/data/corporate-actions/{actionId}", new { numerator = 1, denominator = 10 }, Ct);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync(Ct));
        using var ignored = await client.PostAsync($"/api/data/corporate-actions/{actionId}/ignore", Ct);
        ignored.StatusCode.ShouldBe(HttpStatusCode.OK, await ignored.Content.ReadAsStringAsync(Ct));
        using var detected = await client.PostAsync("/api/data/corporate-actions/detect", Ct);
        detected.StatusCode.ShouldBe(HttpStatusCode.OK, await detected.Content.ReadAsStringAsync(Ct));
        using var imported = await client.PostAsync("/api/data/corporate-actions/import", new StringContent("symbol,exDate,kind,numerator,denominator\nABB,2026-03-01,bonus,1,1\n", System.Text.Encoding.UTF8, "text/csv"), Ct);
        (await imported.ReadAsync<XDrishti.Application.Market.Actions.ImportCorporateActionsResult>(Ct)).Imported.ShouldBe(1);
        (await ScalarAsync(owner, "SELECT count(*) FROM market.corporate_actions")).ShouldBe(2L);
        (await ScalarAsync(owner, "SELECT string_agg(event, ',' ORDER BY at, id) FROM accounts.account_events WHERE event LIKE 'CorporateAction%'")).ShouldBe("CorporateActionAdded,CorporateActionEdited,CorporateActionIgnored,CorporateActionsImported");
        using var actionList = await client.GetAsync("/api/data/corporate-actions", Ct);
        (await actionList.ReadAsync<XDrishti.Application.Market.Actions.CorporateActionPage>(Ct)).Total.ShouldBe(2);

        using var bars = await client.GetAsync($"/api/data/instruments/{abb}/bars?timeframe=1m&take=3", Ct);
        (await bars.ReadAsync<InstrumentBars>(Ct)).Items.Count.ShouldBe(3);
        using var overview = await client.GetAsync("/api/data/overview", Ct);
        (await overview.ReadAsync<DataOverview>(Ct)).Bars.TotalBars.ShouldBe(1500);

        // Undo as xd_app, on compressed chunks: the role has no DELETE, so the file's CSV bars go through market.undo_csv_bars; the Dhan bars (the 5 that replaced CSV bars in January, the 375 of April) stay,
        // the aggregates follow and the file is pending again.
        using var undone = await client.PostAsync("/api/data/csv/files/ABB_1m.csv.gz/undo", Ct);
        undone.StatusCode.ShouldBe(HttpStatusCode.Accepted, await undone.Content.ReadAsStringAsync(Ct));
        var undoStarted = (await undone.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        await factory.DataJobs.RunPendingAsync(host.Services, Ct);
        using var undoFinished = await client.GetAsync($"/api/data/jobs/{undoStarted.Id}", Ct);
        var undoJob = await undoFinished.ReadAsync<DataJobView>(Ct);
        undoJob.Status.ShouldBe("succeeded", undoJob.Error);
        (undoJob.Csv.BarsRemoved, undoJob.Aggregate.Status).ShouldBe((1120, "succeeded"));
        (await ScalarAsync(owner, "SELECT count(*) FROM market.bars_1m")).ShouldBe(380L);
        (await ScalarAsync(owner, "SELECT count(*) FROM market.bars_1m WHERE source = 'csv'")).ShouldBe(0L);
        (await ScalarAsync(owner, "SELECT count(*) FROM market.bars_agg WHERE timeframe = '1D'")).ShouldBe(2L);
        (await ScalarAsync(owner, "SELECT csv_bars || '/' || dhan_bars FROM market.bar_coverage")).ShouldBe("0/380");
        (await ScalarAsync(owner, "SELECT count(*) FROM market.csv_files WHERE undone_at IS NOT NULL")).ShouldBe(1L);
        (await ScalarAsync(owner, "SELECT detail FROM accounts.account_events WHERE event = 'CsvLoadUndone'")).ShouldBe("CSV load of ABB_1m.csv.gz undone, 1120 bars removed");
    }

    /// <summary>
    /// The chunks of the hypertable are tables of their own in <c>_timescaledb_internal</c>, reachable by name. The runtime role must not be able to delete or
    /// truncate bars through them (the hypertable itself has no DELETE for it): checked by privilege and by trying, for the chunks (ordinary or the compressed
    /// ones that hold the packed rows) that exist now — created after the migrations, so the protection holds for future chunks too.
    /// </summary>
    private static async Task CheckChunkPrivilegesAsync(NpgsqlConnection asApp, string what, bool compressed, int expectedAtLeast)
    {
        var chunks = new List<string>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT format('%I.%I', n.nspname, c.relname)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = '_timescaledb_internal' AND c.relkind = 'r'
              AND (CASE WHEN @compressed THEN c.relname LIKE '\_hyper\_%\_chunk\_compressed' OR c.relname LIKE 'compress\_hyper\_%'
                        ELSE c.relname LIKE '\_hyper\_%\_chunk' END)
            """, asApp))
        {
            command.Parameters.AddWithValue("compressed", compressed);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                chunks.Add(reader.GetString(0));
            }
        }

        chunks.Count.ShouldBeGreaterThanOrEqualTo(expectedAtLeast, what);
        foreach (var chunk in chunks)
        {
            foreach (var privilege in new[] { "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER" })
            {
                (await ScalarAsync(asApp, $"SELECT has_table_privilege('xd_app', '{chunk}', '{privilege}')")).ShouldBe(false, $"{privilege} on {what} {chunk}");
            }

            await ShouldBeRejectedAsync(asApp, () => ExecuteAsync(asApp, $"DELETE FROM {chunk}"), PostgresErrorCodes.InsufficientPrivilege);
            await ShouldBeRejectedAsync(asApp, () => ExecuteAsync(asApp, $"TRUNCATE {chunk}"), PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    private async Task<DataJobView> RunJobAsync(HttpClient client, IServiceProvider services, string[]? steps = null)
    {
        using var started = steps is null ? await client.PostAsync("/api/data/refresh", Ct) : await client.PostJsonAsync("/api/data/refresh", new { steps }, Ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync(Ct));
        var job = (await started.Content.ReadFromJsonAsync<DataJobView>(Ct))!;
        await factory.DataJobs.RunPendingAsync(services, Ct);
        using var finished = await client.GetAsync($"/api/data/jobs/{job.Id}", Ct);
        return await finished.ReadAsync<DataJobView>(Ct);
    }

    private static async Task<Guid> SeedPlainAsync(NpgsqlConnection owner, string ticker)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(owner, $"INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES ('{id}', 'NSE:{ticker}', 'NSE', 'NSE_EQ', 'share', '{ticker}', '{ticker}', 1, '{ticker}', false, now(), now())");
        return id;
    }

    private static async Task<Guid> SeedAsync(NpgsqlConnection owner, string ticker)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(owner, $"INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES ('{id}', 'NSE:{ticker}', 'NSE', 'NSE_EQ', 'share', '{ticker}', '{ticker}', 1, '{ticker}', false, now(), now())");
        await ExecuteAsync(owner, $"INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES ('{id}', 'dhan', '4242', 'NSE_EQ')");
        return id;
    }

    private static async Task ShouldBeRejectedAsync(NpgsqlConnection connection, Func<Task> action, string sqlState)
    {
        var error = await Should.ThrowAsync<PostgresException>(action);
        error.SqlState.ShouldBe(sqlState);
        connection.State.ShouldBe(System.Data.ConnectionState.Open);
    }

    private static async Task CreateScratchDatabaseAsync(NpgsqlConnectionStringBuilder admin, NpgsqlConnectionStringBuilder scratch)
    {
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN CREATE ROLE xd_app NOLOGIN; END IF; END $$");
            await ExecuteAsync(connection, $"ALTER ROLE xd_app LOGIN PASSWORD '{AppPassword}'");
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {ScratchDatabase} WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE {ScratchDatabase}");
            await ExecuteAsync(connection, $"GRANT CONNECT ON DATABASE {ScratchDatabase} TO xd_app");
        }

        await using var scratchConnection = new NpgsqlConnection(scratch.ConnectionString);
        await scratchConnection.OpenAsync(Ct);
        await ExecuteAsync(scratchConnection, "ALTER DEFAULT PRIVILEGES GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app");
        await ExecuteAsync(scratchConnection, "ALTER DEFAULT PRIVILEGES GRANT USAGE, SELECT ON SEQUENCES TO xd_app");
    }

    private static async Task DropScratchDatabaseAsync(NpgsqlConnectionStringBuilder admin)
    {
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {ScratchDatabase} WITH (FORCE)");
        await ExecuteAsync(connection, "ALTER ROLE xd_app NOLOGIN");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }
}
