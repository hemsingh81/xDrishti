using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>
/// P11-01 criterion 11 (grants): the runtime role xd_app (no such role in the test databases, so the test creates it with the production default privileges in a scratch database and runs the real migrations there) can insert and select the nine
/// live-capture and execution-feedback tables (P11-01, P11-02) and cannot update, delete or truncate them; the migration has a working Down.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LiveCaptureGrantsTests(ApiFactory factory)
{
    private const string ScratchDatabase = "xd_live_capture_grants_probe";

    private static readonly string[] Tables = ["live_capture_runs", "live_fills", "live_fill_charges", "live_orders", "live_ticket_outcomes", "live_executions", "live_order_classes", "slippage_estimates", "capacity_scores"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_inserts_and_reads_the_five_tables_and_never_changes_or_removes_a_row()
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
            foreach (var table in Tables)
            {
                foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
                {
                    (await ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'trading.{table}', '{privilege}')")).ShouldBe(expected, $"{table} {privilege}");
                }
            }

            await ExecuteAsync(connection, "SET ROLE xd_app");
            await ExecuteAsync(connection, "RESET ROLE");
            await ExecuteAsync(connection, "DROP TABLE trading.capacity_scores, trading.slippage_estimates, trading.live_order_classes, trading.live_executions, trading.live_ticket_outcomes, trading.live_fill_charges, trading.live_orders, trading.live_fills, trading.live_capture_runs"); // what Down does
        }
        finally
        {
            await DropScratchDatabaseAsync(admin);
        }
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
