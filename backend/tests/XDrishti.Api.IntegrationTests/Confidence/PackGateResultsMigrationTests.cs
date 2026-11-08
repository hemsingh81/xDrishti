using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Domain.Readiness;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>CF-11 row 15 (SC-S4a, ADR 0226): the migration <c>AddPackGateResults</c> adds <c>readiness.pack_gate_results</c> and its stamp trigger; Down removes only them; up again is clean.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class PackGateResultsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261106122936_AddReadiness";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task<int> ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(Ct);
    }

    private const string Insert = "INSERT INTO readiness.pack_gate_results (strategy_id, version, pack_id, status, trade_count, actor, reason) VALUES ('ORB', 1, 'p1', 'PASS', 20, 'owner:me@x.test', 'why')";

    [Fact]
    public async Task The_table_its_index_trigger_and_checks_exist_and_agree_with_the_snapshot()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_packgate_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'readiness' AND tablename = 'pack_gate_results'")).ShouldBe(1);
        (await CountAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'readiness' AND indexname = 'ix_pack_gate_results_strategy_version'")).ShouldBe(1);
        (await CountAsync(connection, "SELECT count(*) FROM pg_trigger WHERE tgname = 'trg_pack_gate_results_stamp' AND NOT tgisinternal")).ShouldBe(1);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse();

        (await ExecAsync(connection, Insert)).ShouldBe(1);
        (await CountAsync(connection, "SELECT count(*) FROM readiness.pack_gate_results WHERE recorded_at IS NOT NULL")).ShouldBe(1);
        foreach (var bad in new[]
        {
            Insert.Replace("'PASS'", "'pass'", StringComparison.Ordinal),
            Insert.Replace("'owner:me@x.test'", "'auto'", StringComparison.Ordinal),
            Insert.Replace(", 1, 'p1'", ", 0, 'p1'", StringComparison.Ordinal),
            Insert.Replace("'why'", "' '", StringComparison.Ordinal),
        })
        {
            await Should.ThrowAsync<PostgresException>(async () => await ExecAsync(connection, bad));
        }
    }

    [Fact]
    public async Task Every_status_in_the_gate_list_is_accepted_by_the_table_check()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_packgate_statuses_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var status in PackGateStatuses.All)
        {
            (await ExecAsync(connection, Insert.Replace("'PASS'", $"'{status}'", StringComparison.Ordinal))).ShouldBe(1, $"the table must accept {status}");
        }
    }

    [Fact]
    public async Task Down_removes_only_the_table_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_packgate_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecAsync(connection, Insert);

        await migrator.MigrateAsync(Before, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'readiness' AND tablename = 'pack_gate_results'")).ShouldBe(0);
        (await CountAsync(connection, "SELECT count(*) FROM pg_tables WHERE schemaname = 'readiness' AND tablename IN ('settings', 'scorecard_snapshots')")).ShouldBe(2, "Down touches nothing else");
        (await CountAsync(connection, "SELECT count(*) FROM pg_proc WHERE proname = 'stamp_pack_gate_result'")).ShouldBe(0);

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM readiness.pack_gate_results")).ShouldBe(0);
        (await ExecAsync(connection, Insert)).ShouldBe(1);
    }
}
