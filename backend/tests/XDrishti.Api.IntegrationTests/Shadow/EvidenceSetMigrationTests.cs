using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Shadow;

/// <summary>AM-01b (ADR 0282): the migration <c>AddEvidenceSets</c> seeds <c>paper.shadow.enabled</c> ON as <c>system:default</c> only into an empty table (sealed against the runtime role and a second seed), keeps the owner's own rows, and writes the set of an epoch that already holds shadow rows.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class EvidenceSetMigrationTests(ApiFactory factory)
{
    private const string Before = "20261113035526_ApplyAutoModeDefaults";

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
    public async Task A_fresh_database_reads_the_shadow_switch_ON_from_one_system_row_and_refuses_any_other_system_row()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_am01b_fresh_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await CountAsync(connection, "SELECT count(*) FROM paper.shadow_settings WHERE actor = 'system:default' AND reason = 'ADR 0282' AND enabled")).ShouldBe(1);
        (await CountAsync(connection, "SELECT count(*) FROM paper.shadow_settings")).ShouldBe(1);
        await Should.ThrowAsync<PostgresException>(() => RunAsync(connection, "INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (false, 'system:default', 'ADR 0282')"));
        await Should.ThrowAsync<PostgresException>(() => RunAsync(connection, "INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (true, 'system:other', 'ADR 0282')"));
        var second = await Should.ThrowAsync<PostgresException>(() => RunAsync(connection, "INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (true, 'system:default', 'ADR 0282')"));
        second.SqlState.ShouldBe("23514", "a second system row into a non-empty table is refused by the trigger");

        await RunAsync(connection, "DELETE FROM paper.shadow_settings");
        (await CountAsync(connection, "SELECT count(*) FROM paper.shadow_settings")).ShouldBe(0);
    }

    [Fact]
    public async Task The_runtime_role_is_refused_the_seed_row_even_into_an_empty_table()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_am01b_role_probe", Ct);
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await RunAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN CREATE ROLE xd_app NOLOGIN; END IF; END $$");
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await RunAsync(connection, "DELETE FROM paper.shadow_settings");
        await RunAsync(connection, "GRANT USAGE ON SCHEMA paper TO xd_app");
        await RunAsync(connection, "GRANT SELECT, INSERT ON paper.shadow_settings TO xd_app");
        await RunAsync(connection, "GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA paper TO xd_app");
        await RunAsync(connection, "GRANT EXECUTE ON FUNCTION pg_advisory_xact_lock(bigint) TO xd_app");
        await RunAsync(connection, "SET LOCAL ROLE xd_app");

        var refused = await Should.ThrowAsync<PostgresException>(() => RunAsync(connection, "INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (true, 'system:default', 'ADR 0282')"));

        refused.SqlState.ShouldBe("23514", "the role clause of the trigger, not the emptiness clause: the table was empty");
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task The_migration_keeps_an_owner_row_seeds_nothing_and_writes_the_set_of_an_epoch_that_already_holds_shadow_rows()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_am01b_owner_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);
        await RunAsync(connection, "INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (false, 'owner:me@x.test', 'off')");
        await RunAsync(connection, "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES ('mig', '2099-03-04', '2099-03-04 03:35:00+00', 1, repeat('a', 64), 'b', true, 'n', 'l', 'probe', '1999-12-31 00:00:00+00')");
        await RunAsync(connection, "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) SELECT id, 1, 'primary', 's', 'BUY', 'DEC', 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}' FROM trading.plans WHERE plan_key = 'mig'");
        await RunAsync(connection, "INSERT INTO paper.shadow_sessions (session_date, n_rows, status, digest, prev_digest, written_at) VALUES ('2099-03-04', 1, 'ok', repeat('a', 64), NULL, '2000-01-01')");
        await RunAsync(connection, "INSERT INTO paper.shadow_entries (session_date, plan_id, plan_item_id, trade_key, ticket_group, cell_key, strategy_hash, symbol, side, time_rule, counts_for_edge, edge_epoch, written_at, row_hash) SELECT '2099-03-04', plan_id, id, 'DEC|S|Buy|2099-03-04', 'primary', 'c', repeat('f', 64), 's', 'BUY', 'v', true, NULL, '2000-01-01', repeat('b', 64) FROM trading.plan_items");

        await migrator.MigrateAsync(null, Ct);

        (await CountAsync(connection, "SELECT count(*) FROM paper.shadow_settings WHERE actor = 'system:default'")).ShouldBe(0, "the owner's OFF row stays the only row");
        (await CountAsync(connection, "SELECT count(*) FROM paper.evidence_sets WHERE edge_epoch = 0 AND mode = 'auto' AND versions = to_jsonb(ARRAY[repeat('f', 64)]) AND first_write_at IS NOT NULL")).ShouldBe(1, "the epoch that holds rows keeps counting them");
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
