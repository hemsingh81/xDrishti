using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>CF-07 (ADR 0208): the migration <c>AddLabNullTrialKind</c> lets the ledger take the trial kind <c>null</c> and nothing else new, agrees with the snapshot, and Down removes the null rows and restores the old check.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LabNullTrialKindMigrationTests(ApiFactory factory)
{
    private const string Before = "20261104154039_AddLabRegistration";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static string Insert(string kind) =>
        $"INSERT INTO lab.test_ledger (strategy_id, version, strategy_hash, lab_run_id, run_key, universe_label, window_from, window_to, method, created_at, trial_kind) VALUES ('NUL', 1, '{new string('a', 64)}', '{Guid.NewGuid()}', '{new string('b', 64)}', 'u', '2025-01-01', '2025-06-30', 'null', now(), '{kind}')";

    [Fact]
    public async Task The_migration_accepts_the_null_kind_and_refuses_an_unknown_one_and_agrees_with_the_model()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_null_kind_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ExecuteAsync(connection, Insert("null"))).ShouldBe(1);
        (await ExecuteAsync(connection, Insert("run"))).ShouldBe(1);
        (await ExecuteAsync(connection, Insert("replan"))).ShouldBe(1);
        await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, Insert("other")));
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        db.Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model.GetEntityTypes().Single(e => e.GetTableName() == "holdout_unlocks");
        model.GetIndexes().Select(i => i.GetDatabaseName()).Order().ToList().ShouldBe(["ix_holdout_unlocks_block", "uq_holdout_unlocks_fresh", "uq_holdout_unlocks_version"]);
        (await CountAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'lab' AND tablename = 'holdout_unlocks' AND indexname IN ('ix_holdout_unlocks_block', 'uq_holdout_unlocks_fresh', 'uq_holdout_unlocks_version')")).ShouldBe(3);
    }

    [Fact]
    public async Task Down_removes_the_null_rows_keeps_the_others_and_restores_the_old_check()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_null_kind_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, Insert("null"));
        await ExecuteAsync(connection, Insert("run"));

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM lab.test_ledger")).ShouldBe(1);
        await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, Insert("null")));

        await migrator.MigrateAsync(null, Ct);

        (await ExecuteAsync(connection, Insert("null"))).ShouldBe(1);
    }
}
