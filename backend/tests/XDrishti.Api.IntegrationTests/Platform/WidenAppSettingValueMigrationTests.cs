using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Platform;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Platform;

/// <summary>
/// Criterion 5 of P3-03f: the migration WidenAppSettingValue takes the value column of <c>platform.app_settings</c> from 500 to 8,000 characters and leaves the rows that were there (the Action-center thresholds, the benchmark instrument, the feed
/// marker) as they were. The database is built up to the migration before it, filled, and migrated; the model snapshot agrees with the model.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class WidenAppSettingValueMigrationTests(ApiFactory factory)
{
    private const string Scratch = "xd_widen_app_setting_probe";

    private const string BeforeMigration = "20261015133215_AttentionRetentionGrant";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_value_column_goes_from_500_to_8000_characters_and_the_existing_rows_are_untouched()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = Scratch, Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {Scratch} WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE {Scratch}");
        }

        try
        {
            await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
            var migrator = db.GetService<IMigrator>();

            await migrator.MigrateAsync(BeforeMigration, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            (await ScalarAsync(connection, Length)).ShouldBe(500);
            await ExecuteAsync(connection, """
                INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES
                    ('portfolio.action_thresholds', '{"nearTargetPct":2}', 'owner@example.invalid', '2026-10-01T00:00:00Z'),
                    ('portfolio.benchmark_instrument', '6f9619ff-8b86-d011-b42d-00c04fc964ff', 'owner@example.invalid', '2026-10-02T00:00:00Z'),
                    ('live.feed.manual_run', '2026-10-03T04:00:00Z', 'owner@example.invalid', '2026-10-03T04:00:00Z')
                """);
            var before = await RowsAsync(connection);

            await migrator.MigrateAsync(null, Ct);

            (await ScalarAsync(connection, Length)).ShouldBe(8000);
            (await RowsAsync(connection)).ShouldBe(before);
            await ExecuteAsync(connection, $"INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('research.cost_sheet.x', '{new string('v', 7_999)}', 'cli:cost-sheet', now())");
            await ExecuteAsync(connection, $"INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('research.cost_sheet.y', '{new string('v', 8_000)}', 'cli:cost-sheet', now())");
            var tooLong = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, $"INSERT INTO platform.app_settings (key, value, updated_by, updated_at) VALUES ('research.cost_sheet.z', '{new string('v', 8_001)}', 'cli:cost-sheet', now())"));
            tooLong.SqlState.ShouldBe(PostgresErrorCodes.StringDataRightTruncation);
            db.Database.HasPendingModelChanges().ShouldBeFalse("the model snapshot agrees with the model");
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {Scratch} WITH (FORCE)");
        }
    }

    [Fact]
    public void The_domain_limit_is_8000_and_a_value_over_it_is_refused_before_the_database_is_touched()
    {
        AppSetting.MaxValueLength.ShouldBe(8000);
        AppSetting.Create("research.cost_sheet.a", new string('v', 7_999), "cli:cost-sheet", DateTimeOffset.UnixEpoch).Value.Length.ShouldBe(7_999);
        Should.Throw<ArgumentException>(() => AppSetting.Create("research.cost_sheet.a", new string('v', 8_001), "cli:cost-sheet", DateTimeOffset.UnixEpoch));
    }

    private const string Length = "SELECT character_maximum_length FROM information_schema.columns WHERE table_schema = 'platform' AND table_name = 'app_settings' AND column_name = 'value'";

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

    private static async Task<List<string>> RowsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT key || '|' || value || '|' || updated_by || '|' || updated_at::text FROM platform.app_settings ORDER BY key", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
