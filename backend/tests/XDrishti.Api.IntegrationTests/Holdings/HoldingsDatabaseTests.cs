using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// The holdings tables against the runtime role and against a database that already has accounts. The test databases have no role xd_app, so these
/// tests create it, recreate the production default privileges (deploy/db/init/10-roles.sh) in a scratch database and run the real migrations there.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingsDatabaseTests(ApiFactory factory)
{
    private const string GrantsDatabase = "xd_portfolio_grants_probe";

    private const string MigrationDatabase = "xd_portfolio_migration_probe";

    private const string BeforeMigration = "20261011125706_AddDefaultPortfolioBuckets";

    private const string HoldingsMigration = "20261011153253_AddPortfolioHoldings";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_has_plain_DML_on_the_holdings_tables()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = GrantsDatabase, Pooling = false };
        await CreateScratchAsync(admin, scratch, GrantsDatabase, withDefaultPrivileges: true);
        try
        {
            await using (var migrated = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString)))
            {
                await DatabaseMigrator.MigrateAsync(migrated.Services, Ct);
            }

            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "SET ROLE xd_app");
            await ExecuteAsync(connection, """
                INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES ('00000000-0000-7000-8000-00000000f001', 'probe', 'Probe', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
                INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES ('00000000-0000-7000-8000-00000000f0b1', 'Probe Bucket', now());
                """);

            await ExecuteAsync(connection, """
                INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
                VALUES ('00000000-0000-7000-8000-00000000f0c1', '00000000-0000-7000-8000-00000000f001', '00000000-0000-7000-8000-00000000f0b1', 'ALL', '1', 'PROBE', 'Probe', 10, 1.5, 2, 'open', now(), now())
                """);
            await ExecuteAsync(connection, "UPDATE portfolio.holdings SET qty = 20, last_price = 3");
            await ExecuteAsync(connection, """
                INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated)
                VALUES ('00000000-0000-7000-8000-00000000f0d1', '00000000-0000-7000-8000-00000000f0c1', '2025-01-01', 20, 1.5, 'import', true, now(), false)
                """);
            await ExecuteAsync(connection, "UPDATE portfolio.holding_lots SET closed_on = '2026-01-01', close_price = 2, estimated = true");
            await ExecuteAsync(connection, """
                INSERT INTO portfolio.holding_syncs (id, account_id, "trigger", requested_by, started_at, status, new_count, increased_count, decreased_count, closed_count, unchanged_count)
                VALUES ('00000000-0000-7000-8000-00000000f0e1', '00000000-0000-7000-8000-00000000f001', 'manual', 'owner@example.invalid', now(), 'running', 0, 0, 0, 0, 0)
                """);
            await ExecuteAsync(connection, "UPDATE portfolio.holding_syncs SET status = 'succeeded', finished_at = now()");
            (await ScalarAsync(connection, "SELECT count(*) FROM portfolio.holding_syncs")).ShouldBe(1L);
            await ExecuteAsync(connection, "DELETE FROM portfolio.holding_syncs");
            await ExecuteAsync(connection, "DELETE FROM portfolio.holding_lots");
            await ExecuteAsync(connection, "DELETE FROM portfolio.holdings");

            foreach (var table in new[] { "holdings", "holding_lots", "holding_syncs" })
            {
                foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
                {
                    (await ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'portfolio.{table}', '{privilege}')")).ShouldBe(true, $"{table} {privilege}");
                }
            }

            (await ScalarAsync(connection, "SELECT has_schema_privilege('xd_app', 'portfolio', 'USAGE')")).ShouldBe(true);
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'portfolio.holdings', 'TRUNCATE')")).ShouldBe(false);
        }
        finally
        {
            await DropScratchAsync(admin, GrantsDatabase);
        }
    }

    [Fact]
    public async Task Accounts_that_exist_before_the_migration_get_the_refresh_off_at_16_00_and_no_first_import_date_and_the_rules_are_enforced()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = MigrationDatabase, Pooling = false };
        await CreateScratchAsync(admin, scratch, MigrationDatabase, withDefaultPrivileges: false);
        try
        {
            await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
            using var scope = host.Services.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();

            await migrator.MigrateAsync(BeforeMigration, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            (await ScalarAsync(connection, "SELECT count(*) FROM information_schema.schemata WHERE schema_name = 'portfolio'")).ShouldBe(0L);
            await ExecuteAsync(connection, """
                INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES ('00000000-0000-7000-8000-00000000a001', 'dhan-main', 'Dhan · Main', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200),
                       ('00000000-0000-7000-8000-00000000a002', 'second', 'Second', 'Dhan', '1000000002', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
                INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals) VALUES ('00000000-0000-7000-8000-00000000a001', true, false), ('00000000-0000-7000-8000-00000000a002', false, true);
                """);

            await migrator.MigrateAsync(null, Ct);

            var settings = await RowsAsync(connection, "SELECT right(account_id::text, 4) || '|' || enabled || '|' || include_in_totals || '|' || refresh_mode || '|' || refresh_daily_time_ist || '|' || coalesce(first_import_date::text, '-') FROM accounts.portfolio_settings ORDER BY 1");
            settings.ShouldBe(["a001|true|false|off|16:00:00|-", "a002|false|true|off|16:00:00|-"]);
            foreach (var table in new[] { "holdings", "holding_lots", "holding_syncs" })
            {
                (await ScalarAsync(connection, $"SELECT count(*) FROM portfolio.{table}")).ShouldBe(0L);
            }

            await ShouldBeRefusedAsync(connection, "UPDATE accounts.portfolio_settings SET refresh_mode = 'hourly'", "ck_portfolio_settings_refresh_mode");
            await ShouldBeRefusedAsync(connection, "UPDATE accounts.portfolio_settings SET refresh_mode = 'interval'", "ck_portfolio_settings_refresh_mode");
            (await ScalarAsync(connection, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'accounts' AND table_name = 'portfolio_settings' AND column_name = 'refresh_interval_minutes'")).ShouldBe(0L);
            await ExecuteAsync(connection, "INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES ('00000000-0000-7000-8000-0000000000b1', 'B', now())");
            await ExecuteAsync(connection, """
                INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
                VALUES ('00000000-0000-7000-8000-0000000000c1', '00000000-0000-7000-8000-00000000a001', '00000000-0000-7000-8000-0000000000b1', 'ALL', '1', 'S', 'S', 10, 1, 1, 'open', now(), now())
                """);
            await ShouldBeRefusedAsync(connection, """
                INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
                VALUES (gen_random_uuid(), '00000000-0000-7000-8000-00000000a001', '00000000-0000-7000-8000-0000000000b1', 'ALL', '1', 'S2', 'S2', 10, 1, 1, 'open', now(), now())
                """, "ux_holdings_account_security");
            await ShouldBeRefusedAsync(connection, "UPDATE portfolio.holdings SET status = 'gone'", "ck_holdings_status");
            await ShouldBeRefusedAsync(connection, "INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated, closed_on) VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '2025-01-01', 1, 1, 'import', false, now(), false, '2026-01-01')", "ck_holding_lots_closed");
            await ShouldBeRefusedAsync(connection, "INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated, closed_on, close_price) VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '2025-01-01', 1, 1, 'import', false, now(), false, '2024-12-31', 2)", "ck_holding_lots_closed_after_acquired");
            await ShouldBeRefusedAsync(connection, "INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated) VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '2025-01-01', 0, 1, 'import', false, now(), false)", "ck_holding_lots_qty");
            await ShouldBeRefusedAsync(connection, "INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated) VALUES (gen_random_uuid(), '00000000-0000-7000-8000-0000000000c1', '2025-01-01', 1, 1, 'bought', false, now(), false)", "ck_holding_lots_source");
            await ShouldBeRefusedAsync(connection, "DELETE FROM accounts.broker_accounts WHERE slug = 'dhan-main'", "fk_holdings_broker_accounts_account_id");
            await ExecuteAsync(connection, "DELETE FROM portfolio.holdings"); // lots go with their holding (none here)
        }
        finally
        {
            await DropScratchAsync(admin, MigrationDatabase);
        }
    }

    [Fact]
    public async Task The_interval_mode_is_migrated_to_off_and_the_daily_settings_and_the_time_are_kept()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = MigrationDatabase, Pooling = false };
        await CreateScratchAsync(admin, scratch, MigrationDatabase, withDefaultPrivileges: false);
        try
        {
            await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
            using var scope = host.Services.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();

            await migrator.MigrateAsync(HoldingsMigration, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, """
                INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES ('00000000-0000-7000-8000-00000000a001', 'one', 'One', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, false, false, 200),
                       ('00000000-0000-7000-8000-00000000a002', 'two', 'Two', 'Dhan', '1000000002', current_date, '#1A2B3C', now(), now(), false, false, false, 200),
                       ('00000000-0000-7000-8000-00000000a003', 'three', 'Three', 'Dhan', '1000000003', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
                INSERT INTO accounts.portfolio_settings (account_id, enabled, include_in_totals, refresh_mode, refresh_interval_minutes, refresh_daily_time_ist) VALUES
                    ('00000000-0000-7000-8000-00000000a001', true, true, 'interval', 30, '17:45'),
                    ('00000000-0000-7000-8000-00000000a002', true, true, 'daily', 15, '09:30'),
                    ('00000000-0000-7000-8000-00000000a003', true, true, 'off', 5, '16:00');
                """);

            await migrator.MigrateAsync(null, Ct);

            var settings = await RowsAsync(connection, "SELECT right(account_id::text, 4) || '|' || refresh_mode || '|' || refresh_daily_time_ist || '|' || coalesce(first_import_date::text, '-') FROM accounts.portfolio_settings ORDER BY 1");
            settings.ShouldBe(["a001|off|17:45:00|-", "a002|daily|09:30:00|-", "a003|off|16:00:00|-"]);
            await ExecuteAsync(connection, "UPDATE accounts.portfolio_settings SET first_import_date = '2024-04-01' WHERE right(account_id::text, 4) = 'a002'");
            (await ScalarAsync(connection, "SELECT first_import_date::text FROM accounts.portfolio_settings WHERE refresh_mode = 'daily'")).ShouldBe("2024-04-01");
            await ShouldBeRefusedAsync(connection, "UPDATE accounts.portfolio_settings SET refresh_mode = 'interval'", "ck_portfolio_settings_refresh_mode");
        }
        finally
        {
            await DropScratchAsync(admin, MigrationDatabase);
        }
    }

    private static async Task ShouldBeRefusedAsync(NpgsqlConnection connection, string sql, string constraint)
    {
        var error = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        error.ConstraintName.ShouldBe(constraint);
    }

    private static async Task CreateScratchAsync(NpgsqlConnectionStringBuilder admin, NpgsqlConnectionStringBuilder scratch, string database, bool withDefaultPrivileges)
    {
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN CREATE ROLE xd_app NOLOGIN; END IF; END $$");
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE {database}");
        }

        if (withDefaultPrivileges)
        {
            await using var scratchConnection = new NpgsqlConnection(scratch.ConnectionString);
            await scratchConnection.OpenAsync(Ct);
            await ExecuteAsync(scratchConnection, "ALTER DEFAULT PRIVILEGES GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app");
            await ExecuteAsync(scratchConnection, "ALTER DEFAULT PRIVILEGES GRANT USAGE, SELECT ON SEQUENCES TO xd_app");
        }
    }

    private static async Task DropScratchAsync(NpgsqlConnectionStringBuilder admin, string database)
    {
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
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

    private static async Task<List<string>> RowsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
