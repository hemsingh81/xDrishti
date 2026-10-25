using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>Spec P7-19 criteria 6 and 11: a plan item stored before AddTicketLevels survives the migration with no levels and passes the new check; Down drops the six columns and the check.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class TicketLevelsMigrationTests(ApiFactory factory)
{
    private const string Scratch = "xd_ticket_levels_migration_probe";

    private const string Before = "20261024054955_AddSuggestionSeen";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_legacy_item_gets_null_levels_passes_the_check_and_down_removes_the_columns_and_the_check()
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
            var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();

            await migrator.MigrateAsync(Before, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES ('legacy', '2026-10-23', '2026-10-23 03:35:00+00', 1, repeat('a', 64), 'b', true, 'n', 'l', 'x', now())");
            await ExecuteAsync(connection, "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) SELECT id, 1, 'primary', 's', 'BUY', 'x', 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}' FROM trading.plans WHERE plan_key = 'legacy'");

            await migrator.MigrateAsync(null, Ct);

            (await ScalarAsync(connection, "SELECT count(*) FROM trading.plan_items WHERE entry_ref_price IS NULL AND stop_level IS NULL AND stop_anchor IS NULL AND target_level IS NULL AND reward_risk IS NULL AND levels_session IS NULL")).ShouldBe(1L);
            (await ScalarAsync(connection, "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_plan_items_levels'")).ShouldBe(1L);

            await migrator.MigrateAsync(Before, Ct);

            (await ScalarAsync(connection, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'trading' AND table_name = 'plan_items' AND column_name IN ('entry_ref_price', 'stop_level', 'stop_anchor', 'target_level', 'reward_risk', 'levels_session')")).ShouldBe(0L);
            (await ScalarAsync(connection, "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_plan_items_levels'")).ShouldBe(0L);
            (await ScalarAsync(connection, "SELECT count(*) FROM trading.plan_items")).ShouldBe(1L);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {Scratch} WITH (FORCE)");
        }
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
