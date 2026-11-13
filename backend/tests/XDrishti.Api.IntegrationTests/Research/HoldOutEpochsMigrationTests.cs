using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// HO-01 (ADR 0283): the migration <c>AddHoldOutEpochs</c> keeps the stored row as block 1, marks a second row left by the ADR 0207 race as an orphan, leaves every unlock row as it was, creates the insert-only release table, allows one first block and one successor per
/// block, agrees with the snapshot, and refuses to go down while a release exists.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldOutEpochsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261112120000_AddShadowScores";
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

    [Fact]
    public async Task The_stored_row_becomes_block_one_a_second_row_is_an_orphan_and_old_unlock_rows_are_untouched()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_holdout_epochs_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(Before, Ct);
        await ExecuteAsync(connection, $"INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at) VALUES ('2026-06-01', '2024-10-01', '2026-10-30', 5, {Hash}, '2026-11-01 10:00+00'), ('2026-05-01', '2024-10-01', '2026-10-30', 6, {Hash}, '2026-11-01 10:00:01+00')"); // the earliest-created row has the LATER start: it is block 1, as every reader saw it before
        await ExecuteAsync(connection, $"INSERT INTO lab.holdout_unlocks (strategy_id, version, kind, unlocked_at, mode, actor, setting_hash, reused_window, block_start, lab_run_id, result) VALUES ('ORB', 1, 'strategy', now(), 'owner', 'owner:x', {Hash}, false, '2026-05-01', gen_random_uuid(), '{{}}'), ('ORB', 2, 'strategy', now(), 'owner', 'owner:x', {Hash}, true, '2026-05-01', gen_random_uuid(), '{{}}')");

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT start_date::text || '|' || coalesce(prev_start::text, '-') || '|' || orphan::text FROM lab.holdout_blocks ORDER BY start_date")).ShouldBe(["2026-05-01|-|true", "2026-06-01|-|false"]);
        (await scope.ServiceProvider.GetRequiredService<IHoldOutStore>().CurrentAsync(Ct))!.Start.ShouldBe(new DateOnly(2026, 6, 1), "the store reads the earliest-created row, not the earliest start (AC2: the cut is unchanged)");
        var embargo = new XDrishti.Domain.Confidence.HoldOutOptions().TrainingEmbargoDays;
        var gate = await scope.ServiceProvider.GetRequiredService<IRunStore>().StoreAsync(StoreKit.ToStore(StoredRunFixtures.Run(to: new DateOnly(2026, 6, 1).AddDays(-1 - embargo)) with { IsTraining = true }), Ct);
        gate.IsSuccess.ShouldBeTrue(gate.IsFailure ? gate.Error.Message : string.Empty); // a window that ends at the cut of the later-start block is clear; the orphan's earlier cut would have refused it
        (await ListAsync(connection, "SELECT version::text || '|' || reused_window::text FROM lab.holdout_unlocks ORDER BY version")).ShouldBe(["1|false", "2|true"]);
        (await ListAsync(connection, "SELECT indexname FROM pg_indexes WHERE schemaname = 'lab' AND tablename = 'holdout_blocks' ORDER BY 1")).ShouldBe(["pk_holdout_blocks", "uq_holdout_blocks_first", "uq_holdout_blocks_prev", "uq_holdout_blocks_start"]);
        await Should.ThrowAsync<PostgresException>(async () => await ExecuteAsync(connection, $"INSERT INTO lab.holdout_blocks (start_date, history_first, history_last, length_months, setting_hash, created_at) VALUES ('2026-07-01', '2024-10-01', '2026-10-30', 6, {Hash}, now())"), "a second first block is refused");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_is_clean_without_a_release_and_refused_while_one_exists()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_holdout_epochs_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(null, Ct);
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'lab' AND table_name = 'holdout_releases'")).ShouldBeEmpty();
        await migrator.MigrateAsync(null, Ct);
        await ExecuteAsync(connection, $"INSERT INTO lab.holdout_releases (block_start, released_at, mode, actor, setting_hash, looked, next_block_start) VALUES ('2026-05-01', now(), 'owner', 'owner:x', {Hash}, true, '2026-10-31')");

        var refused = await Should.ThrowAsync<PostgresException>(async () => await migrator.MigrateAsync(Before, Ct));

        refused.MessageText.ShouldContain("release exists");
        (await ListAsync(connection, "SELECT block_start::text FROM lab.holdout_releases")).ShouldBe(["2026-05-01"]);
    }
}
