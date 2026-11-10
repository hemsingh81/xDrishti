using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>AU-12 (ADR 0248): <c>AddSimAllJobs</c> on a scratch database goes down and up again cleanly, the model snapshot is unchanged by it (raw SQL), and a chunk is unique per (job, strategy, version, instrument).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class SimAllJobsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261109203104_AddSimRunPurpose";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task Up_creates_the_two_tables_down_removes_them_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_simall_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = context.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        const string tables = "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'research' AND table_name IN ('sim_all_jobs', 'sim_all_chunks')";

        (await ScalarAsync(connection, tables)).ShouldBe(2);
        context.Database.HasPendingModelChanges().ShouldBeFalse("the migration is raw SQL and the model snapshot agrees");

        await migrator.MigrateAsync(Before, Ct);
        (await ScalarAsync(connection, tables)).ShouldBe(0);
        (await ScalarAsync(connection, "SELECT count(*) FROM pg_proc WHERE proname = 'guard_sim_all_job'")).ShouldBe(0);

        await migrator.MigrateAsync(null, Ct);
        (await ScalarAsync(connection, tables)).ShouldBe(2);
        await ExecuteAsync(connection, "INSERT INTO research.sim_all_jobs (id, state, window_from, window_to, hold_out_start, workers, started_by, created_at, updated_at) VALUES ('00000000-0000-7000-8000-000000000001', 'running', '2020-01-01', '2020-02-01', '2021-01-01', 2, 'owner:x@y.z', now(), now())");
        var insert = "INSERT INTO research.sim_all_chunks (job_id, strategy_id, version, instrument_id, strategy_hash, ordinal) VALUES ('00000000-0000-7000-8000-000000000001', 'ORB', 1, '00000000-0000-7000-8000-0000000000a1', repeat('a', 64), 1)";
        await ExecuteAsync(connection, insert);

        var twice = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, insert));
        twice.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }
}
