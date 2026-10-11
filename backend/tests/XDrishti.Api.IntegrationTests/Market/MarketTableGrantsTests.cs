using System.Net;
using System.Net.Http.Json;
using Npgsql;
using XDrishti.Api.Auth;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Refresh;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>
/// The instrument master works with the production runtime role xd_app (DML only, no DDL): the test creates the role, recreates the production default
/// privileges (deploy/db/init/10-roles.sh) in a scratch database, runs the real migrations there as the owner and then runs a complete refresh
/// through the API connected as xd_app (temporary table, binary COPY, advisory lock, merge).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class MarketTableGrantsTests(ApiFactory factory)
{
    private const string ScratchDatabase = "xd_market_grants_probe";

    private const string AppPassword = "app-role-probe-only";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_has_plain_DML_on_the_market_tables_but_no_DDL_and_a_refresh_works_as_that_role()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = ScratchDatabase, Pooling = false };
        await CreateScratchDatabaseAsync(admin, scratch);
        try
        {
            await using (var migrated = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString)))
            {
                await DatabaseMigrator.MigrateAsync(migrated.Services, Ct);
            }

            await using (var owner = new NpgsqlConnection(scratch.ConnectionString))
            {
                await owner.OpenAsync(Ct);
                (await ScalarAsync(owner, "SELECT count(*) FROM pg_extension WHERE extname = 'pg_trgm'")).ShouldBe(1L);
            }

            await using (var connection = new NpgsqlConnection(scratch.ConnectionString))
            {
                await connection.OpenAsync(Ct);
                await ExecuteAsync(connection, "SET ROLE xd_app");
                await CheckPrivilegesAsync(connection);
            }

            await RunRefreshAsAppRoleAsync(new NpgsqlConnectionStringBuilder(scratch.ConnectionString) { Username = "xd_app", Password = AppPassword });
        }
        finally
        {
            await DropScratchDatabaseAsync(admin);
        }
    }

    private static async Task CheckPrivilegesAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, """
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
            VALUES ('00000000-0000-7000-8000-00000000a001', 'NSE:PROBE', 'NSE', 'NSE_EQ', 'share', 'PROBE', 'Probe', 1, 'PROBE', false, now(), now())
            """);
        await ExecuteAsync(connection, "UPDATE market.instruments SET retired_at = now() WHERE canonical_symbol = 'NSE:PROBE'");
        await ExecuteAsync(connection, "INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES ('00000000-0000-7000-8000-00000000a001', 'dhan', '1', 'NSE_EQ')");
        await ExecuteAsync(connection, "UPDATE market.instrument_broker_ids SET broker_id = '2'");
        await ExecuteAsync(connection, "DELETE FROM market.instrument_broker_ids");
        await ExecuteAsync(connection, "DELETE FROM market.instruments");

        await InsertRunAsync(connection, "00000000-0000-7000-8000-00000000b001", "running");
        await ShouldBeRejectedAsync(connection, () => InsertRunAsync(connection, "00000000-0000-7000-8000-00000000b002", "running"), PostgresErrorCodes.UniqueViolation); // one running refresh
        await ExecuteAsync(connection, "UPDATE market.master_refreshes SET status = 'failed', finished_at = now(), phase = NULL");
        await InsertRunAsync(connection, "00000000-0000-7000-8000-00000000b003", "running"); // possible again once the first ended
        await ShouldBeRejectedAsync(connection, () => InsertRunAsync(connection, "00000000-0000-7000-8000-00000000b004", "weird"), PostgresErrorCodes.CheckViolation);
        await ExecuteAsync(connection, "DELETE FROM market.master_refreshes");

        // What the importer needs: a temporary table to copy into.
        await ExecuteAsync(connection, "CREATE TEMP TABLE probe_stage (a int) ON COMMIT DROP");
        (await ScalarAsync(connection, "SELECT pg_try_advisory_xact_lock(1)")).ShouldBe(true);

        // No DDL in the market schema.
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "CREATE TABLE market.intruder (a int)"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DROP TABLE market.instruments"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "ALTER TABLE market.instruments ADD COLUMN x int"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "TRUNCATE market.instruments"), PostgresErrorCodes.InsufficientPrivilege);

        foreach (var table in new[] { "instruments", "instrument_broker_ids", "master_refreshes" })
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                (await ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'market.{table}', '{privilege}')")).ShouldBe(true, $"{privilege} on {table}");
            }
        }
    }

    private async Task RunRefreshAsAppRoleAsync(NpgsqlConnectionStringBuilder asApp)
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", asApp.ConnectionString));
        (await OwnerBootstrap.ProvisionAsync(host.Services)).ShouldBe(0);
        var client = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.18.250.88");
        using var login = await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
        login.EnsureSuccessStatusCode();
        var session = (await login.Content.ReadFromJsonAsync<XDrishti.Application.Auth.SessionInfo>(Ct))!;
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={ApiFactory.SessionCookieValue(login)}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);

        using var started = await client.PostAsync("/api/instruments/refresh", Ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync(Ct));
        await factory.RefreshJobs.RunPendingAsync(host.Services, Ct);

        using var statusResponse = await client.GetAsync("/api/instruments/refresh/status", Ct);
        var status = await statusResponse.ReadAsync<RefreshStatusView>(Ct);
        status.Last!.Status.ShouldBe("succeeded", status.Last.Error);
        status.Last.Added.ShouldBe(60);
        status.TotalActive.ShouldBe(60);
    }

    private static Task InsertRunAsync(NpgsqlConnection connection, string id, string status) => ExecuteAsync(connection, $$"""
        INSERT INTO market.master_refreshes (id, "trigger", "force", requested_by, started_at, status, phase, rows_read, added, updated, retired, revived, unchanged,
            skipped_by_scope, collisions, sector_matched, sector_unmatched, fno_underlyings, total_active, kept_by_type, warnings, rules_version, sectors_applied)
        VALUES ('{{id}}', 'manual', false, 'probe', now(), '{{status}}', 'downloading', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '{}'::jsonb, '[]'::jsonb, 1, false)
        """);

    private static async Task ShouldBeRejectedAsync(NpgsqlConnection connection, Func<Task> action, string sqlState)
    {
        // A failed statement aborts the surrounding transaction only when there is one; these run autocommit.
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
