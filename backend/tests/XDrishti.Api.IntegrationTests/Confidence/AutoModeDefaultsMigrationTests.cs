using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>AM-01a (ADR 0282): the migration <c>ApplyAutoModeDefaults</c> seeds <c>confidence.kill.auto</c> ON as <c>system:default</c> only into an empty table and refuses any other system row, including a second seed row.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AutoModeDefaultsMigrationTests(ApiFactory factory)
{
    private const string Before = "20261112120000_AddShadowScores";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    [Fact]
    public async Task A_fresh_database_reads_the_kill_switch_ON_from_one_system_row_and_empty_reads_OFF()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_am01a_fresh_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await CountAsync(connection, "SELECT count(*) FROM confidence.kill_settings WHERE actor = 'system:default' AND reason = 'ADR 0282' AND auto")).ShouldBe(1);
        (await CountAsync(connection, "SELECT count(*) FROM confidence.kill_settings")).ShouldBe(1);

        // Only the one exact system row is allowed: not OFF, not another reason, not another system actor.
        await Should.ThrowAsync<PostgresException>(() => RunAsync(connection, "INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (false, 'system:default', 'ADR 0282')"));
        await Should.ThrowAsync<PostgresException>(() => RunAsync(connection, "INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (true, 'system:other', 'ADR 0282')"));
        var second = await Should.ThrowAsync<PostgresException>(() => RunAsync(connection, "INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (true, 'system:default', 'ADR 0282')"));
        second.SqlState.ShouldBe("23514", "a second system row into a non-empty table is refused by the trigger");

        await RunAsync(connection, "DELETE FROM confidence.kill_settings");
        (await CountAsync(connection, "SELECT count(*) FROM confidence.kill_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task The_runtime_role_is_refused_the_seed_row_even_into_an_empty_table()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_am01a_role_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await RunAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN CREATE ROLE xd_app NOLOGIN; END IF; END $$");
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await RunAsync(connection, "DELETE FROM confidence.kill_settings");
        await RunAsync(connection, "GRANT USAGE ON SCHEMA confidence TO xd_app"); // the scratch database has no init script: give the role what the real one has, inside the transaction that is rolled back
        await RunAsync(connection, "GRANT SELECT, INSERT ON confidence.kill_settings TO xd_app");
        await RunAsync(connection, "GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA confidence TO xd_app");
        await RunAsync(connection, "GRANT EXECUTE ON FUNCTION pg_advisory_xact_lock(bigint) TO xd_app");
        await RunAsync(connection, "SET LOCAL ROLE xd_app");

        var refused = await Should.ThrowAsync<PostgresException>(() => RunAsync(connection, "INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (true, 'system:default', 'ADR 0282')"));

        refused.SqlState.ShouldBe("23514", "the role clause of the trigger, not the emptiness clause: the table was empty");
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task The_migration_keeps_an_owner_row_and_seeds_nothing_when_the_table_is_not_empty()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_am01a_owner_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);
        await RunAsync(connection, "INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (false, 'owner:me@x.test', 'off')");

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM confidence.kill_settings WHERE actor = 'system:default'")).ShouldBe(0, "the owner's OFF row stays the newest and only row");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("data only: the snapshot does not change");
    }
}
