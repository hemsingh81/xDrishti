using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P5-02, Contract: the migration <c>AddLabRuns</c> creates <c>lab.lab_runs</c> with its columns, checks, indexes (the partial unique one on the key) and key to the stored version, agrees with the model snapshot, and is reversible.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabRunsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261022090059_AddLabStrategies";

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
    public async Task The_migration_creates_the_table_with_its_columns_checks_indexes_and_key_and_agrees_with_the_model()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_labruns_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, "SELECT column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'lab' AND table_name = 'lab_runs'")).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("id", "uuid"), C("key", "character"), C("strategy_id", "text"), C("version", "integer"), C("strategy_hash", "character"), C("universe", "jsonb"), C("window_from", "date"), C("window_to", "date"),
                C("method", "text"), C("config_hash", "character"), C("engine_version", "text"), C("state", "text"), C("stage", "text", true), C("progress_done", "integer"), C("progress_total", "integer"),
                C("sim_run_keys", "ARRAY"), C("counts", "jsonb", true), C("problems", "jsonb"), C("stages", "jsonb"), C("labels", "jsonb"), C("error", "text", true), C("host", "text", true),
                C("cancel_requested", "boolean"), C("created_at", "timestamp with time zone"), C("started_at", "timestamp with time zone", true), C("finished_at", "timestamp with time zone", true),
                C("updated_at", "timestamp with time zone"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'lab.lab_runs'::regclass AND contype <> 'n' ORDER BY 1")).ShouldBe(
            ["ck_lab_runs_key", "ck_lab_runs_progress", "ck_lab_runs_state", "ck_lab_runs_window", "fk_lab_runs_strategy_version", "pk_lab_runs"]);
        (await ListAsync(connection, "SELECT indexname FROM pg_indexes WHERE schemaname = 'lab' AND tablename = 'lab_runs' ORDER BY 1")).ShouldBe(
            ["ix_lab_runs_queued", "ix_lab_runs_strategy_created", "ix_lab_runs_strategy_hash", "pk_lab_runs", "uq_lab_runs_active_key"]);
        var unique = (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'uq_lab_runs_active_key'")).Single();
        unique.ShouldContain("UNIQUE");
        unique.ShouldContain("(key)");
        unique.ShouldContain("'queued'");
        unique.ShouldContain("'running'");
        unique.ShouldContain("'succeeded'");
        unique.ShouldNotContain("'failed'");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_removes_only_the_run_table_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_labruns_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' ORDER BY 1")).ShouldBe(["strategies", "strategy_versions"]);

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' ORDER BY 1")).ShouldBe(["lab_results", "lab_runs", "strategies", "strategy_versions", "test_ledger"]);
    }
}
