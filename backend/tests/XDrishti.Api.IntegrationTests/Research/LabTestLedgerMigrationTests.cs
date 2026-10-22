using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>Spec P5-03, criterion 1 and Contract: the migration <c>AddLabTestLedger</c> creates <c>lab.test_ledger</c> with its columns, checks and indexes (unique run id), agrees with the snapshot, and is reversible.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabTestLedgerMigrationTests(ApiFactory factory)
{
    private const string Before = "20261022105624_AddLabRuns";

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
    public async Task The_migration_creates_the_ledger_with_its_columns_checks_and_indexes_and_agrees_with_the_model()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_ledger_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, "SELECT column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'lab' AND table_name = 'test_ledger'")).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("id", "uuid"), C("strategy_id", "text"), C("version", "integer"), C("strategy_hash", "character"), C("parent_hash", "character", true), C("lab_run_id", "uuid"), C("run_key", "character"),
                C("universe_label", "text"), C("window_from", "date"), C("window_to", "date"), C("method", "text"), C("created_at", "timestamp with time zone"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'lab.test_ledger'::regclass AND contype <> 'n' ORDER BY 1")).ShouldBe(["ck_test_ledger_hash", "pk_test_ledger"]);
        (await ListAsync(connection, "SELECT indexname FROM pg_indexes WHERE schemaname = 'lab' AND tablename = 'test_ledger' ORDER BY 1")).ShouldBe(["ix_test_ledger_strategy_created", "pk_test_ledger", "uq_test_ledger_run"]);
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'uq_test_ledger_run'")).Single().ShouldContain("UNIQUE");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_removes_only_the_ledger_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_ledger_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' ORDER BY 1")).ShouldBe(["lab_runs", "strategies", "strategy_versions"]);

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' ORDER BY 1")).ShouldBe(["lab_results", "lab_runs", "strategies", "strategy_versions", "test_ledger"]);
    }
}
