using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>CF-11 part 1 (ADR 0221): the migration <c>AddReadiness</c> adds schema <c>readiness</c> with its two tables, indexes and triggers, agrees with the model snapshot (it maps no entity), and Down removes only that schema; up again is clean.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ReadinessMigrationTests(ApiFactory factory)
{
    private const string Before = "20261106042217_AddDecayAndKill";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task The_schema_has_its_tables_indexes_triggers_and_agrees_with_the_snapshot()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_readiness_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'readiness' AND tablename IN ('settings', 'scorecard_snapshots')")).ShouldBe(2);
        (await CountAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'readiness' AND indexname IN ('uq_scorecard_snapshots_daily', 'ix_scorecard_snapshots_subject_strategy_id')")).ShouldBe(2);
        (await CountAsync(connection, "SELECT count(*) FROM pg_trigger WHERE tgname IN ('trg_readiness_settings_stamp', 'trg_scorecard_snapshots_stamp') AND NOT tgisinternal")).ShouldBe(2);
        (await CountAsync(connection, "SELECT count(*) FROM pg_constraint WHERE connamespace = 'readiness'::regnamespace AND contype = 'c'")).ShouldBeGreaterThanOrEqualTo(3);
        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'confidence'")).ShouldBe(3, "the confidence schema keeps its three tables");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration maps no entity, so the snapshot does not change");
    }

    [Fact]
    public async Task Down_removes_only_the_schema_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_readiness_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO readiness.settings (enforce, actor, reason) VALUES (true, 'owner:me@x.test', 'why')", connection))
        {
            await insert.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_namespace WHERE nspname = 'readiness'")).ShouldBe(0);
        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'confidence'")).ShouldBe(3, "Down touches nothing outside its schema");
        (await CountAsync(connection, "SELECT count(*) FROM pg_proc WHERE proname IN ('stamp_setting', 'stamp_snapshot')")).ShouldBe(0, "the functions went with the schema");

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM readiness.settings")).ShouldBe(0);
        await using var again = new NpgsqlCommand("INSERT INTO readiness.settings (enforce, actor, reason) VALUES (false, 'owner:me@x.test', 'again')", connection);
        (await again.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }
}
