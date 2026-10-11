using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>
/// The audit trail is append-only for the runtime role xd_app. The test databases have no such role, so this test creates it,
/// recreates the production default privileges (deploy/db/init/10-roles.sh) in a scratch database, runs the real migrations
/// there and then acts as xd_app.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AuditTableGrantsTests(ApiFactory factory)
{
    private const string ScratchDatabase = "xd_audit_grants_probe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_can_insert_and_read_audit_events_but_not_change_or_delete_them()
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

            await ExecuteAsync(connection, "INSERT INTO auth.audit_events (id, at, event) VALUES (gen_random_uuid(), now(), 'LoginFailed')");
            (await ScalarAsync(connection, "SELECT count(*) FROM auth.audit_events")).ShouldBe(1L);
            await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "UPDATE auth.audit_events SET detail = 'tampered'"))
                .ContinueWith(t => t.Result.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege), Ct);
            await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "DELETE FROM auth.audit_events"))
                .ContinueWith(t => t.Result.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege), Ct);

            // The other auth tables stay fully usable for the runtime role.
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'auth.owner_accounts', 'UPDATE')")).ShouldBe(true);
            (await ScalarAsync(connection, "SELECT has_table_privilege('xd_app', 'auth.sessions', 'DELETE')")).ShouldBe(true);
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
