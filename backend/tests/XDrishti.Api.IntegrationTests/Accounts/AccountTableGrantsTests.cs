using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>
/// The account audit trail is append-only for the runtime role xd_app, like the sign-in audit trail; the other account tables (the broker
/// vault, connections and pending sign-ins included) are plain DML.
/// The test databases have no such role, so this test creates it, recreates the production default privileges (deploy/db/init/10-roles.sh)
/// in a scratch database, runs the real migrations there and then acts as xd_app.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AccountTableGrantsTests(ApiFactory factory)
{
    private const string ScratchDatabase = "xd_accounts_grants_probe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_can_use_the_account_tables_but_can_only_insert_and_read_account_events()
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

            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "SET ROLE xd_app");

            // Seeded rows are readable (profiles only: there are no seeded buckets).
            (await ScalarAsync(connection, "SELECT count(*) FROM accounts.risk_profiles")).ShouldBe(2L);
            (await ScalarAsync(connection, "SELECT count(*) FROM accounts.portfolio_buckets")).ShouldBe(0L);

            // Accounts and their settings: full DML.
            await ExecuteAsync(connection, """
                INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES ('00000000-0000-7000-8000-00000000f001', 'probe', 'Probe', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200)
                """);
            await ExecuteAsync(connection, "UPDATE accounts.broker_accounts SET name = 'Probe 2' WHERE slug = 'probe'");
            await ExecuteAsync(connection, """
                INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments)
                VALUES ('00000000-0000-7000-8000-00000000f001', false, 'Paper', 'Fixed', false, false, 0, 0, 0)
                """);
            await ExecuteAsync(connection, "UPDATE accounts.risk_profiles SET name = 'Standard' WHERE name = 'Standard'");

            // The broker vault: full DML (secrets are replaced and deleted).
            await ExecuteAsync(connection, """
                INSERT INTO accounts.broker_secrets (account_id, name, ciphertext, key_version, updated_at)
                VALUES ('00000000-0000-7000-8000-00000000f001', 'api_key', '\x0102'::bytea, 1, now())
                """);
            await ExecuteAsync(connection, "UPDATE accounts.broker_secrets SET ciphertext = '\\x0304'::bytea WHERE name = 'api_key'");
            (await ScalarAsync(connection, "SELECT count(*) FROM accounts.broker_secrets")).ShouldBe(1L);
            await ExecuteAsync(connection, "DELETE FROM accounts.broker_secrets WHERE name = 'api_key'");
            // Broker connections and pending sign-ins: full DML.
            await ExecuteAsync(connection, """
                INSERT INTO accounts.broker_connections (account_id, status, expires_at)
                VALUES ('00000000-0000-7000-8000-00000000f001', 'Connected', now() + interval '1 hour')
                """);
            await ExecuteAsync(connection, "UPDATE accounts.broker_connections SET status = 'NotConnected'");
            await ExecuteAsync(connection, "DELETE FROM accounts.broker_connections");
            await ExecuteAsync(connection, """
                INSERT INTO accounts.pending_connects (id, account_id, session_id, created_at, expires_at)
                VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000f001', gen_random_uuid(), now(), now() + interval '10 minutes')
                """);
            await ExecuteAsync(connection, "UPDATE accounts.pending_connects SET consumed_at = now() WHERE consumed_at IS NULL");
            await ExecuteAsync(connection, "DELETE FROM accounts.pending_connects");
            await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES (gen_random_uuid(), 'Probe Bucket', now())");
            await ExecuteAsync(connection, "DELETE FROM accounts.portfolio_buckets WHERE name = 'Probe Bucket'");
            // The default bucket of an account is created, renamed and archived at runtime.
            await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_buckets (id, name, created_at, default_for_account_id) VALUES (gen_random_uuid(), 'Probe · Portfolio', now(), '00000000-0000-7000-8000-00000000f001')");
            await ExecuteAsync(connection, "UPDATE accounts.portfolio_buckets SET name = 'Probe 2 · Portfolio', archived_at = now() WHERE default_for_account_id = '00000000-0000-7000-8000-00000000f001'");

            // The audit trail: insert and read only.
            await ExecuteAsync(connection, "INSERT INTO accounts.account_events (id, at, account_id, event, detail, actor) VALUES (gen_random_uuid(), now(), '00000000-0000-7000-8000-00000000f001', 'AccountCreated', 'probe', 'owner@example.invalid')");
            await ExecuteAsync(connection, "INSERT INTO accounts.account_events (id, at, event, actor) VALUES (gen_random_uuid(), now(), 'BucketAdded', 'owner@example.invalid')");
            (await ScalarAsync(connection, "SELECT count(*) FROM accounts.account_events")).ShouldBe(2L);
            await ShouldBeDeniedAsync(connection, "UPDATE accounts.account_events SET detail = 'tampered'");
            await ShouldBeDeniedAsync(connection, "DELETE FROM accounts.account_events");
            await ShouldBeDeniedAsync(connection, "TRUNCATE accounts.account_events");

            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'accounts.broker_accounts', 'DELETE')")).ShouldBe(true);
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'accounts.broker_secrets', 'UPDATE')")).ShouldBe(true);
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'accounts.broker_secrets', 'DELETE')")).ShouldBe(true);
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'accounts.account_events', 'UPDATE')")).ShouldBe(false);
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'accounts.account_events', 'DELETE')")).ShouldBe(false);
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'accounts.account_events', 'INSERT')")).ShouldBe(true);
        }
        finally
        {
            await DropScratchDatabaseAsync(admin);
        }
    }

    private static async Task ShouldBeDeniedAsync(NpgsqlConnection connection, string sql)
    {
        var error = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        error.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static async Task CreateScratchDatabaseAsync(NpgsqlConnectionStringBuilder admin, NpgsqlConnectionStringBuilder scratch)
    {
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN CREATE ROLE xd_app NOLOGIN; END IF; END $$");
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {ScratchDatabase} WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE {ScratchDatabase}");
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
