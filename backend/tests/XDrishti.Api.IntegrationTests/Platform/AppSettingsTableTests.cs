using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Platform;

/// <summary>The settings table of migration AddPortfolioBenchmarkSetting (P2-07): its columns, and rows that the runtime role can read, insert and update but never delete.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AppSettingsTableTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'platform' AND tablename = 'app_settings' AND indexname = 'pk_app_settings'", Ct)).ShouldBe(1L);
    }

    [Fact]
    public async Task The_app_role_can_read_insert_and_update_the_settings_but_never_delete_them()
    {
        await AppRoleProbe.RunAsync(
            factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('portfolio.benchmark_instrument', 'abc', 'owner@example.invalid', now())", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE platform.app_settings SET value = 'def'", Ct);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT value FROM platform.app_settings", Ct)).ShouldBe("def");

                var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, "DELETE FROM platform.app_settings", Ct));
                refused.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
            },
            Ct);
    }
}
