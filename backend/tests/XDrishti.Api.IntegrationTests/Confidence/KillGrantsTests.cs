using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Confidence;

/// <summary>
/// CF-12 part 12b (ADR 0219): what the runtime role <c>xd_app</c> may do to the three tables of schema <c>confidence</c>, and what the database refuses whatever the application does. Runs on the dedicated server of <see cref="ResearchGrantsTests"/>, where the role has the
/// default privileges of the init script. The switch rows and the daily evaluations are insert-only; a proposal is insert-only except for four decision columns and one move from proposed; the proposal-only rule (the CF-09 failure boundary) can never be decided by <c>auto</c>, and an
/// automatic decision needs the newest switch row to be ON and to be the one recorded.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class KillGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Hash = new('a', 64);

    private static string Proposal(string strategy, string rule = "cost_stress", string day = "2026-11-06", bool? only = null) =>
        $"INSERT INTO confidence.kill_proposals (strategy, rule, proposal_only, trades, text, cells, settings_hash, series_trades, proposed_on) VALUES ('{strategy}', '{rule}', {(only ?? rule == "failure_boundary").ToString().ToLowerInvariant()}, 100, 't', '[\"c\"]', '{Hash}', 130, '{day}') RETURNING id";

    private static async Task<string?> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await StoreKit.ExecuteAsync(connectionString, sql, Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private Task<long> SwitchAsync(bool auto) => ScalarAsync(server.App, $"INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES ({auto.ToString().ToLowerInvariant()}, 'owner:me@x.test', 'why') RETURNING id");

    [Fact]
    public async Task The_runtime_role_can_never_write_the_system_default_row_so_it_cannot_turn_kill_auto_back_on()
    {
        await SwitchAsync(false);

        var state = await SqlStateAsync(server.App, "INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (true, 'system:default', 'ADR 0282')");

        state.ShouldBe("23514");
    }

    [Fact]
    public async Task The_schema_holds_the_three_tables_with_no_table_level_update_or_delete_for_the_runtime_role_and_five_updatable_columns()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var tables = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname = 'confidence' ORDER BY 1", connection);
        var names = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        names.ShouldBe(["decay_checks", "kill_proposals", "kill_settings"]);
        foreach (var name in names)
        {
            // a proposal can be inserted only column by column (below), never as a whole row
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", name != "kill_proposals"), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", "confidence." + name);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, $"{privilege} on {name}");
            }
        }

        await using var insertable = new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema = 'confidence' AND table_name = 'kill_proposals' AND has_column_privilege('xd_app', 'confidence.kill_proposals', column_name, 'INSERT') ORDER BY 1", connection);
        var insertColumns = new List<string>();
        await using (var reader = await insertable.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                insertColumns.Add(reader.GetString(0));
            }
        }

        insertColumns.ShouldBe(["cells", "epoch_id", "limit_value", "proposal_only", "proposed_on", "rule", "series_trades", "settings_hash", "strategy", "text", "trades", "value"]);

        await using var columns = new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema = 'confidence' AND table_name = 'kill_proposals' AND has_column_privilege('xd_app', 'confidence.kill_proposals', column_name, 'UPDATE') ORDER BY 1", connection);
        var updatable = new List<string>();
        await using (var reader = await columns.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                updatable.Add(reader.GetString(0));
            }
        }

        updatable.ShouldBe(["actor", "auto_setting_id", "decided_at", "dismiss_reason", "state"]);
    }

    [Fact]
    public async Task As_xd_app_the_switch_and_the_evaluations_are_insert_only_and_the_switch_takes_only_an_owner()
    {
        var first = await SwitchAsync(true);
        var second = await SwitchAsync(false);

        second.ShouldBeGreaterThan(first, "the id follows commit order");
        (await SqlStateAsync(server.App, "INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (true, 'auto', 'why')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO confidence.kill_settings (auto, actor, reason) VALUES (true, 'owner:me@x.test', '  ')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        foreach (var statement in new[] { "UPDATE confidence.kill_settings SET auto = true", "DELETE FROM confidence.kill_settings", "TRUNCATE confidence.kill_settings", "UPDATE confidence.decay_checks SET trades = 1", "DELETE FROM confidence.decay_checks", "TRUNCATE confidence.decay_checks" })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        const string check = "INSERT INTO confidence.decay_checks (strategy, checked_on, settings_hash, edge_state, trades, verdicts) VALUES ('GR1', '2026-11-06', '{0}', 'alert', 130, '[]')";
        (await SqlStateAsync(server.App, string.Format(System.Globalization.CultureInfo.InvariantCulture, check, Hash))).ShouldBeNull();
        (await SqlStateAsync(server.App, string.Format(System.Globalization.CultureInfo.InvariantCulture, check, Hash))).ShouldBe(PostgresErrorCodes.UniqueViolation, "one evaluation per strategy and day");
        (await SqlStateAsync(server.App, string.Format(System.Globalization.CultureInfo.InvariantCulture, check, "zz"))).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_proposal_is_one_per_strategy_rule_and_day_and_the_runtime_role_can_only_decide_it_once()
    {
        var id = await ScalarAsync(server.App, Proposal("GR2"));

        (await SqlStateAsync(server.App, Proposal("GR2"))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await SqlStateAsync(server.App, Proposal("GR2", day: "2026-11-07"))).ShouldBeNull();
        (await SqlStateAsync(server.App, Proposal("GR2", rule: "failure_boundary", only: false))).ShouldBe(PostgresErrorCodes.CheckViolation, "only the failure boundary is proposal-only, and it always is");
        (await SqlStateAsync(server.App, Proposal("GR2", rule: "other"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        foreach (var statement in new[] { "UPDATE confidence.kill_proposals SET strategy = 'x'", "UPDATE confidence.kill_proposals SET cells = '[]'", "UPDATE confidence.kill_proposals SET proposal_only = true", "DELETE FROM confidence.kill_proposals", "TRUNCATE confidence.kill_proposals" })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'owner:me@x.test' WHERE id = {id}")).ShouldBeNull();
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'dismissed', actor = 'owner:me@x.test' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation, "a decided proposal is not changed again");
        (await ScalarAsync(server.App, $"SELECT count(*) FROM confidence.kill_proposals WHERE id = {id} AND state = 'applied' AND decided_at IS NOT NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task The_runtime_role_cannot_insert_an_already_decided_proposal()
    {
        var on = await SwitchAsync(true);
        const string decided = "INSERT INTO confidence.kill_proposals (strategy, rule, proposal_only, trades, text, cells, settings_hash, series_trades, proposed_on, {0}) VALUES ('GR5', 'cost_stress', false, 100, 't', '[]', '{1}', 130, '2026-11-06', {2})";
        var args = new[]
        {
            ("state, actor, decided_at", "'applied', 'owner:me@x.test', now()"),
            ("state", "'applied'"),
            ("actor", "'owner:me@x.test'"),
            ("decided_at", "now()"),
            ("state, actor, decided_at, auto_setting_id", $"'applied', 'auto', now(), {on}"),
        };

        foreach (var (columns, values) in args)
        {
            (await SqlStateAsync(server.App, string.Format(System.Globalization.CultureInfo.InvariantCulture, decided, columns, Hash, values))).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, columns);
        }

        (await ScalarAsync(server.App, "SELECT count(*) FROM confidence.kill_proposals WHERE strategy = 'GR5'")).ShouldBe(0);
        (await ScalarAsync(server.App, Proposal("GR5"))).ShouldBeGreaterThan(0);
        (await ScalarAsync(server.App, "SELECT count(*) FROM confidence.kill_proposals WHERE strategy = 'GR5' AND state = 'proposed' AND actor IS NULL AND decided_at IS NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task The_database_refuses_an_automatic_decision_on_the_failure_boundary_without_a_switch_that_is_on_and_recorded_or_for_a_dismissal()
    {
        var on = await SwitchAsync(true);
        var boundary = await ScalarAsync(server.App, Proposal("GR3", "failure_boundary"));
        var cost = await ScalarAsync(server.App, Proposal("GR3", "cost_stress"));
        var drawdown = await ScalarAsync(server.App, Proposal("GR3", "drawdown"));
        var decay = await ScalarAsync(server.App, Proposal("GR3", "decay_twice"));

        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {on} WHERE id = {boundary}")).ShouldBe(PostgresErrorCodes.CheckViolation, "the CF-09 boundary is never decided by auto");
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'auto' WHERE id = {cost}")).ShouldBe(PostgresErrorCodes.CheckViolation, "an automatic decision records the switch row");
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'owner:me@x.test', auto_setting_id = {on} WHERE id = {cost}")).ShouldBe(PostgresErrorCodes.CheckViolation, "an owner decision names no switch row");
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'dismissed', actor = 'auto', auto_setting_id = {on} WHERE id = {cost}")).ShouldBe(PostgresErrorCodes.CheckViolation, "auto never dismisses");
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {on + 9999} WHERE id = {cost}")).ShouldNotBeNull("no such switch row");
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {on} WHERE id = {cost}")).ShouldBeNull("the newest switch row is ON and is the one recorded");

        var off = await SwitchAsync(false);
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {on} WHERE id = {drawdown}")).ShouldBe(PostgresErrorCodes.CheckViolation, "the switch was turned OFF since");
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {off} WHERE id = {drawdown}")).ShouldBe(PostgresErrorCodes.CheckViolation, "the recorded row is OFF");
        var again = await SwitchAsync(true);
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {on} WHERE id = {decay}")).ShouldBe(PostgresErrorCodes.CheckViolation, "the recorded row is not the newest");
        (await SqlStateAsync(server.App, $"UPDATE confidence.kill_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {again} WHERE id = {decay}")).ShouldBeNull();
    }
}
