using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Platform;

/// <summary>The settings table of migration AddPortfolioBenchmarkSetting (P2-07): its columns, and rows that the runtime role can read, insert and update but never delete.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AppSettingsTableTests(ApiFactory factory)
{
    private const string ScratchDatabase = "xd_app_settings_grants_probe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    [Fact]
    public async Task The_table_has_the_columns_of_a_key_value_setting_with_its_author_and_time()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        var columns = new List<string>();
        await using (var command = new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema = 'platform' AND table_name = 'app_settings' ORDER BY ordinal_position", connection))
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                columns.Add(reader.GetString(0));
            }
        }

        columns.ShouldBe(["key", "value", "updated_by", "updated_at"]);
        (await ScalarAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'platform' AND tablename = 'app_settings' AND indexname = 'pk_app_settings'")).ShouldBe(1L);
    }

    [Fact]
    public async Task The_app_role_can_read_insert_and_update_the_settings_but_never_delete_them()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = ScratchDatabase, Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN CREATE ROLE xd_app NOLOGIN; END IF; END $$");
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {ScratchDatabase} WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE {ScratchDatabase}");
        }

        try
        {
            await using (var setup = new NpgsqlConnection(scratch.ConnectionString))
            {
                await setup.OpenAsync(Ct);
                await ExecuteAsync(setup, "ALTER DEFAULT PRIVILEGES GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app");
                await ExecuteAsync(setup, "ALTER DEFAULT PRIVILEGES GRANT USAGE, SELECT ON SEQUENCES TO xd_app");
            }

            await using (var migrated = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString)))
            {
                await DatabaseMigrator.MigrateAsync(migrated.Services, Ct);
            }

            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "SET ROLE xd_app");
            await ExecuteAsync(connection, "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('portfolio.benchmark_instrument', 'abc', 'owner@example.invalid', now())");
            await ExecuteAsync(connection, "UPDATE platform.app_settings SET value = 'def'");
            (await ScalarAsync(connection, "SELECT value FROM platform.app_settings")).ShouldBe("def");

            var refused = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, "DELETE FROM platform.app_settings"));
            refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {ScratchDatabase} WITH (FORCE)");
        }
    }
}
