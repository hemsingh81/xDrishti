using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// AU-11 (ADR 0243): <c>AddSimRunPurpose</c> on a scratch database. Every run stored before it reads as <c>research</c> and stays visible in the AI view, the model and the snapshot agree, a training run is hidden from the view, and Down refuses while a
/// training run exists (dropping the column would make it evidence), restores the old views and the old ledger check once none does, and Up again is clean.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class SimRunPurposeMigrationTests(ApiFactory factory)
{
    private const string Before = "20261109130000_AddLearnedSettingChanges";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task InsertRunAsync(NpgsqlConnection connection, string key, string? purpose)
    {
        var column = purpose is null ? string.Empty : ", purpose";
        var value = purpose is null ? string.Empty : $", '{purpose}'";
        await using var command = new NpgsqlCommand($"""
            INSERT INTO research.sim_runs (run_key, engine_version, record_schema_version, trade_schema_version, strategy_id, definition_sha256, from_date, to_date, universe_kind, universe_label, instruments, instruments_run,
                trades_in_window, candidates_in_window, trade_count, open_at_end_count, acknowledge_csv_adjusted, fills, record_json, record_sha256, trades_sha256, created_at, created_by{column})
            VALUES ('{key}', 'sim-5', 5, 4, 'ORB', '{new string('d', 64)}', '2024-01-01', '2024-02-01', 'basket', 'probe', 1, 1, 0, 0, 0, 0, false, 'modelled', 'x', '{new string('e', 64)}', '{new string('f', 64)}', now(), 'probe'{value})
            """, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task Existing_runs_read_as_research_the_view_fences_training_down_refuses_while_one_exists_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_purpose_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = context.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);
        await InsertRunAsync(connection, new string('1', 64), null); // a run stored before the migration
        await migrator.MigrateAsync(null, Ct);

        (await ScalarAsync(connection, "SELECT count(*) FROM research.sim_runs WHERE purpose = 'research'")).ShouldBe(1);
        (await ScalarAsync(connection, "SELECT count(*) FROM aiv.sim_runs")).ShouldBe(1);
        context.Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");

        await InsertRunAsync(connection, new string('2', 64), "training");
        (await ScalarAsync(connection, "SELECT count(*) FROM aiv.sim_runs")).ShouldBe(1);

        var refused = await Should.ThrowAsync<PostgresException>(() => migrator.MigrateAsync(Before, Ct));
        refused.MessageText.ShouldContain("Training runs exist");

        await using (var delete = new NpgsqlCommand("DELETE FROM research.sim_runs WHERE purpose = 'training'", connection))
        {
            await delete.ExecuteNonQueryAsync(Ct);
        }

        await migrator.MigrateAsync(Before, Ct);
        (await ScalarAsync(connection, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'research' AND table_name = 'sim_runs' AND column_name = 'purpose'")).ShouldBe(0);
        (await ScalarAsync(connection, "SELECT count(*) FROM aiv.sim_runs")).ShouldBe(1);

        await migrator.MigrateAsync(null, Ct);
        (await ScalarAsync(connection, "SELECT count(*) FROM research.sim_runs WHERE purpose = 'research'")).ShouldBe(1);
    }
}
