using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>CF-03 (ADR 0205): the migration <c>AddLabVariantStats</c> creates <c>lab.variant_stats</c> with its columns, checks and indexes (unique run id and variant hash), agrees with the snapshot, and is reversible without touching any other table.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabVariantStatsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261104111125_AddCellStatsShrinkage";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
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

    private static string C(string name, string type, bool nullable = false) => $"{name}|{type}|{(nullable ? "YES" : "NO")}";

    [Fact]
    public async Task The_migration_creates_the_table_with_its_columns_checks_and_indexes_and_agrees_with_the_model()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_variant_stats_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, "SELECT column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'lab' AND table_name = 'variant_stats'")).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("id", "uuid"), C("strategy_id", "text"), C("run_id", "uuid"), C("variant_hash", "character"), C("n", "integer"), C("mean_r", "double precision", true), C("sd_r", "double precision", true),
                C("skew", "double precision", true), C("kurt", "double precision", true), C("sr_per_trade", "double precision", true), C("method", "text"), C("created_at", "timestamp with time zone"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'lab.variant_stats'::regclass AND contype <> 'n' ORDER BY 1")).ShouldBe(["ck_variant_stats_finite", "ck_variant_stats_shape", "pk_variant_stats"]);
        (await ListAsync(connection, "SELECT indexname FROM pg_indexes WHERE schemaname = 'lab' AND tablename = 'variant_stats' ORDER BY 1")).ShouldBe(["ix_variant_stats_strategy_created", "pk_variant_stats", "uq_variant_stats_run_variant"]);
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'uq_variant_stats_run_variant'")).Single().ShouldContain("UNIQUE");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_removes_only_the_table_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_variant_stats_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' ORDER BY 1")).ShouldBe(["expression_profiles", "lab_results", "lab_runs", "strategies", "strategy_versions", "test_ledger"]);

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' ORDER BY 1")).ShouldBe(["expression_profiles", "lab_results", "lab_runs", "strategies", "strategy_versions", "test_ledger", "variant_stats"]);
    }
}
