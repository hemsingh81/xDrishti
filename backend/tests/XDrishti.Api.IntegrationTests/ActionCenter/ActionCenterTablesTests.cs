using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.ActionCenter;

/// <summary>The two tables of migration AddActionCenterAndPriceAlerts (P2-09): their columns and checks, and the grants — the runtime role reads, inserts and updates them and can never delete (a decision is cleared and an alert triggered or cancelled by an update).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class ActionCenterTablesTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string[]> ColumnsAsync(NpgsqlConnection connection, string table)
    {
        var columns = new List<string>();
        await using var command = new NpgsqlCommand($"SELECT column_name FROM information_schema.columns WHERE table_schema = 'portfolio' AND table_name = '{table}' ORDER BY ordinal_position", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            columns.Add(reader.GetString(0));
        }

        return [.. columns];
    }

    [Fact]
    public async Task The_tables_have_the_columns_of_the_contract()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);

        (await ColumnsAsync(connection, "action_states")).ShouldBe(["rule", "key", "state", "snoozed_until", "updated_by", "updated_at"]);
        (await ColumnsAsync(connection, "price_alerts")).ShouldBe(["id", "instrument_id", "direction", "level", "note", "status", "created_at", "created_by", "triggered_at", "trigger_price"]);
        (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'portfolio' AND tablename = 'price_alerts' AND indexname = 'ix_price_alerts_status_instrument'", Ct)).ShouldBe(1L);
        (await AppRoleProbe.ScalarAsync(connection, "SELECT count(*) FROM pg_indexes WHERE schemaname = 'portfolio' AND tablename = 'action_states' AND indexname = 'pk_action_states'", Ct)).ShouldBe(1L);
    }

    [Theory]
    [InlineData("INSERT INTO portfolio.action_states (rule, key, state, updated_by, updated_at) VALUES ('r', 'k', 'paused', 'o', now())", "ck_action_states_state")]
    [InlineData("INSERT INTO portfolio.action_states (rule, key, state, updated_by, updated_at) VALUES ('r', 'k', 'snoozed', 'o', now())", "ck_action_states_snoozed_until")]
    [InlineData("INSERT INTO portfolio.action_states (rule, key, state, snoozed_until, updated_by, updated_at) VALUES ('r', 'k', 'done', date '2026-10-19', 'o', now())", "ck_action_states_snoozed_until")]
    [InlineData("INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by) VALUES (gen_random_uuid(), gen_random_uuid(), 'sideways', 1, 'active', now(), 'o')", "ck_price_alerts_direction")]
    [InlineData("INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by) VALUES (gen_random_uuid(), gen_random_uuid(), 'at_or_below', 0, 'active', now(), 'o')", "ck_price_alerts_level")]
    [InlineData("INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by) VALUES (gen_random_uuid(), gen_random_uuid(), 'at_or_below', 10000000.01, 'active', now(), 'o')", "ck_price_alerts_level")]
    [InlineData("INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by) VALUES (gen_random_uuid(), gen_random_uuid(), 'at_or_below', 1, 'waiting', now(), 'o')", "ck_price_alerts_status")]
    [InlineData("INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by) VALUES (gen_random_uuid(), gen_random_uuid(), 'at_or_below', 1, 'triggered', now(), 'o')", "ck_price_alerts_triggered")]
    public async Task The_database_refuses_what_the_rules_never_produce(string sql, string constraint)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);

        var refused = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, sql, Ct));

        (refused.SqlState, refused.ConstraintName).ShouldBe((PostgresErrorCodes.CheckViolation, constraint));
    }

    [Fact]
    public async Task The_app_role_can_read_insert_and_update_decisions_and_alerts_but_never_delete_them()
    {
        await AppRoleProbe.RunAsync(
            factory,
            async connection =>
            {
                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO portfolio.action_states (rule, key, state, updated_by, updated_at) VALUES ('below_cost', 'k', 'done', 'owner@example.invalid', now())", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.action_states SET state = 'cleared' WHERE rule = 'below_cost'", Ct);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT state FROM portfolio.action_states", Ct)).ShouldBe("cleared");
                await AppRoleProbe.ExecuteAsync(connection, "INSERT INTO portfolio.price_alerts (id, instrument_id, direction, level, status, created_at, created_by) VALUES (gen_random_uuid(), gen_random_uuid(), 'at_or_below', 1200, 'active', now(), 'o')", Ct);
                await AppRoleProbe.ExecuteAsync(connection, "UPDATE portfolio.price_alerts SET status = 'triggered', triggered_at = now(), trigger_price = 1190.5 WHERE status = 'active'", Ct);
                (await AppRoleProbe.ScalarAsync(connection, "SELECT status FROM portfolio.price_alerts", Ct)).ShouldBe("triggered");

                var states = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, "DELETE FROM portfolio.action_states", Ct));
                var alerts = await Should.ThrowAsync<PostgresException>(() => AppRoleProbe.ExecuteAsync(connection, "DELETE FROM portfolio.price_alerts", Ct));
                (states.SqlState, alerts.SqlState).ShouldBe((PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.InsufficientPrivilege));
            },
            Ct);
    }
}
