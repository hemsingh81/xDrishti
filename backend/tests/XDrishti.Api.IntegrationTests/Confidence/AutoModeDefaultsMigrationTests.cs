using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>AM-01a (ADR 0282): the migration <c>ApplyAutoModeDefaults</c> seeds <c>confidence.kill.auto</c> ON as <c>system:default</c> only into an empty table, refuses any other system row, and enables a service only where the owner never changed it.</summary>
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

    private static string Service(string id, bool enabled, string by) =>
        $"INSERT INTO platform.service_schedules (service_id, enabled, schedule, retries, timeout_minutes, catch_up, notify_on_failure, parameters, updated_by, updated_at) VALUES ('{id}', {enabled}, '{{\"type\":\"daily\",\"time\":\"18:00\"}}', 0, 15, true, true, '{{}}', '{by}', now())";

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

        await RunAsync(connection, "DELETE FROM confidence.kill_settings");
        (await CountAsync(connection, "SELECT count(*) FROM confidence.kill_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task The_migration_keeps_an_owner_row_and_enables_only_services_the_owner_never_changed()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_am01a_owner_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);
        await RunAsync(connection, "INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (false, 'owner:me@x.test', 'off')");
        await RunAsync(connection, Service("ml-monitor", false, "owner:me@x.test"));
        await RunAsync(connection, Service("paper-capture", false, "system:first-start"));
        await RunAsync(connection, Service("eod", false, "system:first-start"));

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM confidence.kill_settings WHERE actor = 'system:default'")).ShouldBe(0, "the owner's OFF row stays the newest and only row");
        (await CountAsync(connection, "SELECT count(*) FROM platform.service_schedules WHERE service_id = 'ml-monitor' AND NOT enabled AND updated_by = 'owner:me@x.test'")).ShouldBe(1, "a service the owner paused stays paused");
        (await CountAsync(connection, "SELECT count(*) FROM platform.service_schedules WHERE service_id = 'paper-capture' AND enabled AND updated_by = 'system:default'")).ShouldBe(1);
        (await CountAsync(connection, "SELECT count(*) FROM platform.service_schedules WHERE service_id = 'eod' AND NOT enabled")).ShouldBe(1, "a service outside the six is never touched");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("data only: the snapshot does not change");
    }
}
