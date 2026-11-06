using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>CF-10 part 10d-1 (ADR 0220): the migration <c>AddMlRollback</c> adds <c>research.ml_switch_settings</c>, <c>rollback_proposals</c> and <c>ml_monitor_checks</c> with their keys, indexes, checks and triggers, agrees with the model snapshot (it maps no entity), and Down removes only those tables and functions; up again is clean.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class MlRollbackMigrationTests(ApiFactory factory)
{
    private const string Before = "20261106042217_AddDecayAndKill";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task The_tables_have_their_unique_keys_checks_triggers_and_agree_with_the_snapshot()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_ml_rollback_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'research' AND tablename IN ('ml_switch_settings', 'rollback_proposals', 'ml_monitor_checks')")).ShouldBe(3);
        (await CountAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'research' AND indexname IN ('uq_rollback_proposals_model_trigger_day', 'uq_ml_monitor_checks_model_day', 'ix_rollback_proposals_state_id', 'ix_rollback_proposals_model_id_id', 'ix_ml_switch_settings_name_id')")).ShouldBe(5);
        (await CountAsync(connection, "SELECT count(*) FROM pg_trigger WHERE tgname IN ('trg_ml_switch_settings_stamp', 'trg_rollback_proposals_guard') AND NOT tgisinternal")).ShouldBe(2);
        (await CountAsync(connection, "SELECT count(*) FROM pg_constraint WHERE connamespace = 'research'::regnamespace AND conname LIKE 'fk_rollback_proposals_%'")).ShouldBe(3);
        (await CountAsync(connection, "SELECT count(*) FROM pg_constraint WHERE connamespace = 'research'::regnamespace AND contype = 'c' AND (conname LIKE 'ck_rollback_proposals_%' OR conname LIKE 'ck_ml_switch_settings_%' OR conname LIKE 'ck_ml_monitor_checks_%')")).ShouldBe(5);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration maps no entity, so the snapshot does not change");
    }

    [Fact]
    public async Task Down_removes_only_those_tables_and_functions_and_up_again_is_clean_with_every_switch_off_because_no_row_means_off()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_ml_rollback_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.auto_rollback', true, 'owner:me@x.test', 'why')", connection))
        {
            await insert.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'research' AND tablename IN ('ml_switch_settings', 'rollback_proposals', 'ml_monitor_checks')")).ShouldBe(0);
        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'research' AND tablename IN ('ml_influence_checks', 'model_versions', 'model_designations', 'learning_changelog')")).ShouldBe(4, "Down touches nothing else");
        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'confidence'")).ShouldBe(3, "nor the schema before it");
        (await CountAsync(connection, "SELECT count(*) FROM pg_proc WHERE proname IN ('stamp_ml_switch_setting', 'guard_rollback_proposal')")).ShouldBe(0);

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM research.ml_switch_settings")).ShouldBe(0, "no row means OFF");
        await using var again = new NpgsqlCommand("INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.influence.enabled', false, 'owner:me@x.test', 'again')", connection);
        (await again.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }
}
