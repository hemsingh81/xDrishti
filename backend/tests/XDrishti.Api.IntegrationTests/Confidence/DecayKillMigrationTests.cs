using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>CF-12 part 12b (ADR 0219): the migration <c>AddDecayAndKill</c> adds schema <c>confidence</c> with its three tables, indexes and triggers, agrees with the model snapshot (it maps no entity), and Down removes only that schema; up again is clean.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DecayKillMigrationTests(ApiFactory factory)
{
    private const string Before = "20261105193544_AddMlSafety";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task The_schema_has_its_tables_unique_keys_triggers_and_agrees_with_the_snapshot()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_decay_kill_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'confidence' AND tablename IN ('kill_settings', 'kill_proposals', 'decay_checks')")).ShouldBe(3);
        (await CountAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'confidence' AND indexname IN ('uq_kill_proposals_strategy_rule_day', 'uq_decay_checks_strategy_day', 'ix_kill_proposals_state_id', 'ix_kill_proposals_strategy_rule_id')")).ShouldBe(4);
        (await CountAsync(connection, "SELECT count(*) FROM pg_trigger WHERE tgname IN ('trg_kill_settings_stamp', 'trg_kill_proposals_guard') AND NOT tgisinternal")).ShouldBe(2);
        (await CountAsync(connection, "SELECT count(*) FROM pg_constraint WHERE connamespace = 'confidence'::regnamespace AND contype = 'c'")).ShouldBeGreaterThanOrEqualTo(5);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration maps no entity, so the snapshot does not change");
    }

    [Fact]
    public async Task Down_removes_only_the_schema_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_decay_kill_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await using (var insert = new NpgsqlCommand("INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (true, 'owner:me@x.test', 'why')", connection))
        {
            await insert.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_namespace WHERE nspname = 'confidence'")).ShouldBe(0);
        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'research' AND tablename = 'ml_influence_checks'")).ShouldBe(1, "Down touches nothing outside the schema");
        (await CountAsync(connection, "SELECT count(*) FROM pg_proc WHERE proname IN ('stamp_kill_setting', 'guard_kill_proposal')")).ShouldBe(0, "the functions went with the schema");

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM confidence.kill_settings")).ShouldBe(0);
        await using var again = new NpgsqlCommand("INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (false, 'owner:me@x.test', 'again')", connection);
        (await again.ExecuteNonQueryAsync(Ct)).ShouldBe(1);
    }
}
