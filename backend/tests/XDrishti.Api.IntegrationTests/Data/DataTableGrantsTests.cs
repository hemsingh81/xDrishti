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

    private static readonly string[] Tables = ["bars_1m", "bars_agg", "bar_coverage", "csv_files", "data_jobs"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_runs_the_whole_pipeline_on_the_hypertable_including_compressed_chunks_but_cannot_delete_or_change_tables()
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
        await ExecuteAsync(connection, "RESET ROLE");
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

        using var bars = await client.GetAsync($"/api/data/instruments/{abb}/bars?timeframe=1m&take=3", Ct);
        (await bars.ReadAsync<InstrumentBars>(Ct)).Items.Count.ShouldBe(3);
        using var overview = await client.GetAsync("/api/data/overview", Ct);
        (await overview.ReadAsync<DataOverview>(Ct)).Bars.TotalBars.ShouldBe(1500);
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
