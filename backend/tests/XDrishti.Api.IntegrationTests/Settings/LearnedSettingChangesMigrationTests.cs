using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Settings;

/// <summary>AU-10 (ADR 0247): the migration <c>AddLearnedSettingChanges</c> adds schema <c>config</c> with its table, indexes and guard trigger, agrees with the model snapshot (it maps no entity), and Down removes only that schema; up again is clean.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LearnedSettingChangesMigrationTests(ApiFactory factory)
{
    private const string Before = "20261109120000_AddRecoveryManualTrigger";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task The_schema_has_its_table_indexes_trigger_and_agrees_with_the_snapshot()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_learned_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'config' AND tablename = 'learned_setting_changes'")).ShouldBe(1);
        (await CountAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'config' AND indexname IN ('uq_learned_setting_changes_key_evidence', 'ix_learned_setting_changes_state_id', 'ix_learned_setting_changes_key_id')")).ShouldBe(3);
        (await CountAsync(connection, "SELECT count(*) FROM pg_trigger WHERE tgname = 'trg_learned_setting_changes_guard' AND NOT tgisinternal")).ShouldBe(1);
        (await CountAsync(connection, "SELECT count(*) FROM pg_constraint WHERE connamespace = 'config'::regnamespace AND contype = 'c'")).ShouldBe(2);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration maps no entity, so the snapshot does not change");
    }

    [Fact]
    public async Task Down_removes_only_the_schema_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_learned_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO config.learned_setting_changes (setting_key, old_value, new_value, direction, source, snapshot, snapshot_hash, evidence_ref, evidence_hash) VALUES ('k', '1', '2', 'tighten', 'learning', '{}', repeat('a', 64), 'r', 'h')", connection))
        {
            await insert.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_namespace WHERE nspname = 'config'")).ShouldBe(0);
        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'platform' AND tablename = 'app_settings'")).ShouldBe(1, "Down touches nothing outside the schema");
        (await CountAsync(connection, "SELECT count(*) FROM pg_proc WHERE proname = 'guard_learned_setting_change'")).ShouldBe(0, "the function went with the schema");

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM config.learned_setting_changes")).ShouldBe(0);
        await using var again = new NpgsqlCommand("INSERT INTO config.learned_setting_changes (setting_key, old_value, new_value, direction, source, snapshot, snapshot_hash, evidence_ref, evidence_hash) VALUES ('k', '1', '2', 'tighten', 'learning', '{}', repeat('a', 64), 'r', 'h')", connection);
        (await again.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }
}
