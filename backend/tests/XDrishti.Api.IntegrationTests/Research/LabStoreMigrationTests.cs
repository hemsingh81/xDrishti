using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// Criterion 1 of P3-02d: the migration <c>AddLabStrategies</c> creates <c>lab.strategies</c> and <c>lab.strategy_versions</c> with the columns, checks, keys and indexes of D1 and D2, agrees with the model snapshot and is reversible: <c>Down</c>
/// removes both tables and the schema, and fails, removing nothing, when the schema holds another object.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabStoreMigrationTests(ApiFactory factory)
{
    private const string Before = "20261022065553_AddExitSelections";

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

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static string C(string name, string type, int? length = null, bool nullable = false) => $"{name}|{type}|{length}|{(nullable ? "YES" : "NO")}";

    private static string Columns(string table) =>
        $"SELECT column_name || '|' || data_type || '|' || coalesce(character_maximum_length::text, '') || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'lab' AND table_name = '{table}'";

    [Fact]
    public async Task The_migration_creates_the_two_tables_with_their_columns_checks_keys_and_indexes_and_the_model_has_nothing_more_to_add()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_lab_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' ORDER BY 1")).ShouldBe(["lab_results", "lab_runs", "strategies", "strategy_versions", "test_ledger"]);
        (await ListAsync(connection, Columns("strategies"))).Order(StringComparer.Ordinal).ShouldBe(
            new[] { C("id", "text"), C("style", "text"), C("built_in", "boolean"), C("created_at", "timestamp with time zone") }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, Columns("strategy_versions"))).Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                C("strategy_id", "text"), C("version", "integer"), C("schema_version", "smallint"), C("hash", "character", 64), C("source_text", "text"), C("canonical_json", "text"),
                C("parent_hash", "character", 64, true), C("note", "text", null, true), C("created_at", "timestamp with time zone"),
            }.Order(StringComparer.Ordinal));
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE connamespace = 'lab'::regnamespace AND contype <> 'n' AND conname NOT LIKE '%lab_runs%' AND conname NOT LIKE '%test_ledger%' AND conname NOT LIKE '%lab_results%' ORDER BY 1")).ShouldBe(
        [
            "ck_strategies_id", "ck_strategies_style", "ck_strategy_versions_hash", "ck_strategy_versions_sizes", "ck_strategy_versions_version", "fk_strategy_versions_parent",
            "fk_strategy_versions_strategy", "pk_strategies", "pk_strategy_versions", "uq_strategy_versions_hash",
        ]);
        (await ListAsync(connection, "SELECT indexname FROM pg_indexes WHERE schemaname = 'lab' ORDER BY 1")).ShouldContain("uq_strategies_id_lower");
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE schemaname = 'lab' AND indexname = 'uq_strategies_id_lower'")).Single().ShouldContain("lower(id)");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_removes_both_tables_and_the_schema_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_lab_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'lab'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT count(*)::text FROM information_schema.tables WHERE table_schema = 'research'")).ShouldNotBe(["0"], "Down removes the lab and nothing before it");

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' ORDER BY 1")).ShouldBe(["lab_results", "lab_runs", "strategies", "strategy_versions", "test_ledger"]);
    }

    [Fact]
    public async Task Down_fails_and_removes_nothing_when_the_schema_holds_another_object()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_lab_down_extra_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, "CREATE TABLE lab.later_table (x integer)");

        var failure = await Should.ThrowAsync<Exception>(() => migrator.MigrateAsync(Before, Ct));

        (failure as PostgresException ?? failure.InnerException as PostgresException).ShouldNotBeNull().SqlState.ShouldBe(PostgresErrorCodes.DependentObjectsStillExist);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' ORDER BY 1")).ShouldBe(["later_table", "strategies", "strategy_versions"], "AddLabTestLedger.Down and AddLabRuns.Down had already run in their own transactions; the older Down is what fails and removes nothing");

        await ExecuteAsync(connection, "DROP TABLE lab.later_table");
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'lab'")).ShouldBeEmpty();
    }
}
