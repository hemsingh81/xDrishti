using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// HO-01b (ADR 0283): the migration <c>WidenHoldOutLooks</c> makes the fresh slot one per (block, kind), lets a version look once per block, admits the <c>system</c> kind, adds the nullable <c>reason</c> and the <c>seen_holdout</c> label of the cell rows, leaves old rows as they
/// were, agrees with the snapshot, and refuses to go down while a <c>system</c> row exists.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class WidenHoldOutLooksMigrationTests(ApiFactory factory)
{
    private const string Before = "20261113035946_AddHoldOutEpochs";
    private const string Hash = "'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'";

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

    private static string Unlock(string id, int version, string kind, bool reused, string block, string? reason = null) =>
        $"INSERT INTO lab.holdout_unlocks (strategy_id, version, kind, unlocked_at, mode, actor, setting_hash, reused_window, block_start, lab_run_id, result{(reason is null ? string.Empty : ", reason")}) VALUES ('{id}', {version}, '{kind}', now(), 'owner', 'owner:x', {Hash}, {reused.ToString().ToLowerInvariant()}, '{block}', gen_random_uuid(), '{{}}'{(reason is null ? string.Empty : $", '{reason}'")})";

    [Fact]
    public async Task One_fresh_look_per_kind_and_block_one_look_per_version_and_block_the_system_kind_and_the_new_columns()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_widen_holdout_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(Before, Ct);
        await ExecuteAsync(connection, Unlock("ORB", 1, "strategy", false, "2026-05-01"));
        await ExecuteAsync(connection, Unlock("model-family:tf-1@2026-05-01", 1, "model", true, "2026-05-01"));

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT kind || '|' || reused_window::text || '|' || coalesce(reason, '-') FROM lab.holdout_unlocks ORDER BY kind")).ShouldBe(["model|true|-", "strategy|false|-"], "old rows are untouched and have no reason");
        await ExecuteAsync(connection, Unlock("model-family:tf-2@2026-05-01", 1, "model", false, "2026-05-01")); // the model slot of the block was never fresh: it is free now
        await ExecuteAsync(connection, Unlock("SYS", 1, "system", false, "2026-11-05")); // the system kind is admitted
        await ExecuteAsync(connection, Unlock("ORB", 1, "strategy", false, "2026-11-05")); // a version looks once per block: block 2 is a new look
        await ExecuteAsync(connection, Unlock("ORB", 4, "strategy", true, "2026-11-05", "not_predated"));
        (await ListAsync(connection, "SELECT reason FROM lab.holdout_unlocks WHERE reason IS NOT NULL")).ShouldBe(["not_predated"]);
        await Should.ThrowAsync<PostgresException>(async () => await ExecuteAsync(connection, Unlock("ORB", 2, "strategy", false, "2026-05-01")), "a second fresh strategy look at the block is refused");
        await Should.ThrowAsync<PostgresException>(async () => await ExecuteAsync(connection, Unlock("ORB", 1, "strategy", true, "2026-05-01")), "the same version twice at one block is refused");
        await Should.ThrowAsync<PostgresException>(async () => await ExecuteAsync(connection, Unlock("X", 1, "other", false, "2026-11-05")), "an unknown kind is refused");
        (await ListAsync(connection, "SELECT column_name FROM information_schema.columns WHERE table_schema = 'research' AND table_name = 'cell_stats' AND column_name = 'seen_holdout'")).ShouldBe(["seen_holdout"]);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_is_clean_without_a_system_row_and_refused_while_one_exists()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_widen_holdout_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(null, Ct);
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT column_name FROM information_schema.columns WHERE table_schema = 'lab' AND table_name = 'holdout_unlocks' AND column_name = 'reason'")).ShouldBeEmpty();
        await migrator.MigrateAsync(null, Ct);
        await ExecuteAsync(connection, Unlock("SYS", 1, "system", false, "2026-11-05"));

        var refused = await Should.ThrowAsync<PostgresException>(async () => await migrator.MigrateAsync(Before, Ct));

        refused.MessageText.ShouldContain("cannot be reverted");
        (await ListAsync(connection, "SELECT kind FROM lab.holdout_unlocks")).ShouldBe(["system"]);
    }

    [Fact]
    public async Task Down_is_refused_while_a_not_predated_reason_is_stored()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_widen_holdout_reason_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(null, Ct);
        await ExecuteAsync(connection, Unlock("ORB", 4, "strategy", true, "2026-11-05", "not_predated"));

        var refused = await Should.ThrowAsync<PostgresException>(async () => await migrator.MigrateAsync(Before, Ct));

        refused.MessageText.ShouldContain("cannot be reverted");
    }
}
