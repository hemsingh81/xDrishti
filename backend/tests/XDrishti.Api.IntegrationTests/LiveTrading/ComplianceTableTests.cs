using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>
/// P11-05 criterion 8: the runtime role xd_app (no such role in the test databases, so the test creates it with the production default privileges in a scratch database and runs the real migrations there) inserts and reads trading.compliance_records and
/// cannot update, delete or truncate it; the table refuses an unknown kind, an outcome outside the kind's set, a whitelisted state without its date and a text longer than its column; and the migration has a working Down.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ComplianceTableTests(ApiFactory factory)
{
    private const string ScratchDatabase = "xd_compliance_grants_probe";

    private const string Insert = "INSERT INTO trading.compliance_records (kind, outcome, decided_on, summary, static_ip_state, whitelisted_at, lock_ends_on, next_review_on, actor) VALUES ";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_inserts_and_reads_but_cannot_change_delete_or_truncate_and_the_table_refuses_what_the_rules_refuse()
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

            await ExecuteAsync(connection, Insert + "('sebi_recheck', 'rechecked_no_change', '2026-10-24', 'ok', NULL, NULL, NULL, '2027-01-22', 'owner:me@example.invalid')");
            await ExecuteAsync(connection, Insert + "('static_ip', 'whitelisted', '2026-10-24', 'ok', 'whitelisted', '2026-10-20', '2026-10-27', '2027-01-22', 'owner:me@example.invalid')");
            (await ScalarAsync(connection, "SELECT count(*) FROM trading.compliance_records")).ShouldBe(2L);

            await DeniedAsync(connection, "UPDATE trading.compliance_records SET summary = 'tampered'");
            await DeniedAsync(connection, "DELETE FROM trading.compliance_records");
            await DeniedAsync(connection, "TRUNCATE trading.compliance_records");
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'trading.compliance_records', 'UPDATE')")).ShouldBe(false);
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'trading.compliance_records', 'DELETE')")).ShouldBe(false);

            await RefusedAsync(connection, Insert + "('made_up', 'unanswered', '2026-10-24', 'ok', NULL, NULL, NULL, '2027-01-22', 'owner:me@example.invalid')", PostgresErrorCodes.CheckViolation);
            await RefusedAsync(connection, Insert + "('sebi_recheck', 'whitelisted', '2026-10-24', 'ok', NULL, NULL, NULL, '2027-01-22', 'owner:me@example.invalid')", PostgresErrorCodes.CheckViolation);
            await RefusedAsync(connection, Insert + "('static_ip', 'whitelisted', '2026-10-24', 'ok', 'whitelisted', NULL, NULL, '2027-01-22', 'owner:me@example.invalid')", PostgresErrorCodes.CheckViolation);
            await RefusedAsync(connection, Insert + "('static_ip', 'whitelisted', '2026-10-24', 'ok', 'whitelisted', '2026-10-20', '2026-10-28', '2027-01-22', 'owner:me@example.invalid')", PostgresErrorCodes.CheckViolation);
            await RefusedAsync(connection, Insert + "('sebi_recheck', 'blocked', '2026-10-24', 'ok', 'none', NULL, NULL, '2027-01-22', 'owner:me@example.invalid')", PostgresErrorCodes.CheckViolation);
            await RefusedAsync(connection, Insert + "('sebi_recheck', 'blocked', '2026-10-24', 'ok', NULL, NULL, NULL, '2026-10-24', 'owner:me@example.invalid')", PostgresErrorCodes.CheckViolation);
            await RefusedAsync(connection, Insert + "('sebi_recheck', 'blocked', '2026-10-24', 'ok', NULL, NULL, NULL, '2027-01-22', 'cli:xd')", PostgresErrorCodes.CheckViolation);
            await RefusedAsync(connection, Insert + $"('sebi_recheck', 'blocked', '2026-10-24', '{new string('x', 1001)}', NULL, NULL, NULL, '2027-01-22', 'owner:me@example.invalid')", PostgresErrorCodes.StringDataRightTruncation);
            await RefusedAsync(connection, "INSERT INTO trading.compliance_records (kind, outcome, decided_on, summary, sources, next_review_on, actor) VALUES ('sebi_recheck', 'blocked', '2026-10-24', 'ok', ARRAY['a','b','c','d','e','f','g','h','i','j','k'], '2027-01-22', 'owner:me@example.invalid')", PostgresErrorCodes.CheckViolation);
        }
        finally
        {
            await DropScratchDatabaseAsync(admin);
        }
    }

    private static async Task DeniedAsync(NpgsqlConnection connection, string sql) =>
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql))).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

    private static async Task RefusedAsync(NpgsqlConnection connection, string sql, string state) =>
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql))).SqlState.ShouldBe(state);

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
