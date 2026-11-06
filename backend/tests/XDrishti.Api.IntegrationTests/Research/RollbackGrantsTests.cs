using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// CF-10 part 10d-1 (ADR 0220): what the runtime role <c>xd_app</c> may do to the three new tables of schema <c>research</c>, and what the database refuses whatever the application does. Runs on the dedicated server of <see cref="ResearchGrantsTests"/>, where the role has the default
/// privileges of the init script. The switch rows and the daily evaluations are insert-only; a rollback proposal is insert-only except for four decision columns and one move from proposed; <c>auto</c> never dismisses and decides only while the newest <c>ml.auto_rollback</c> row is ON and is
/// the one recorded; a switch row names an owner.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class RollbackGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Hash = new('a', 64);

    private const string TrainingSet = """
        INSERT INTO research.training_sets (set_id, label_set_id, rule_version, feature_version, basis, sources, required_features, half_life_days, prior_strength, min_trades, credible_level, from_date, to_date, labelled, row_count,
            positives, negatives, unresolved, no_target, dropped_no_features, dropped_unknown, created_at, created_by)
        VALUES (repeat('a', 64), repeat('a', 64), 'tb-1', 'tf-1', 'Gross', 's', 'cell_n', 180, 20, 30, 0.9, '2026-01-01', '2026-06-01', 10, 10, 6, 4, 0, 0, 0, 0, now(), 'test')
        ON CONFLICT DO NOTHING
        """;

    private static string Model(char key) => $"""
        INSERT INTO research.model_versions (version_key, sha256, artifact_bytes, trainer, note, training_set_id, feature_version, feature_width, fit_rows, calibration_rows, train_rows, from_date, to_date, hold_out_start, verdict, used_folds,
            folds_beating_both, folds_needed, pooled_beats_both, pooled_ece_pp, pooled_log_loss, pooled_brier, reasons, report_text, parameters, calibration_scores, calibration_values, importance, trials, purged_rows, created_at, created_by)
        VALUES (repeat('{key}', 64), repeat('{key}', 64), 100, 'FastTree', NULL, repeat('a', 64), 'tf-1', 3, 30, 10, 40, '2026-01-01', '2026-04-01', '2026-05-01', 'Eligible', 6, 5, 4, true, 3.5, 0.65, 0.23, '', 'r', 'p',
            ARRAY[-1.0, 0.0, 1.0], ARRAY[0.3, 0.5, 0.7], ARRAY[0.25, 0.25, 0.25], 1, 4, now(), 'test') RETURNING id
        """;

    private static string Proposal(long model, string trigger = "calibration", string day = "2026-11-06", long? previous = null, bool qualifies = false) =>
        $"INSERT INTO research.rollback_proposals (model_id, trigger, text, cells, previous_champion_id, previous_qualifies, settings_hash, proposed_on) VALUES ({model}, '{trigger}', 't', '[]', {(previous is { } p ? p.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")}, {qualifies.ToString().ToLowerInvariant()}, '{Hash}', '{day}') RETURNING id";

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

    private Task<long> SwitchAsync(string name, bool on) => ScalarAsync(server.App, $"INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('{name}', {on.ToString().ToLowerInvariant()}, 'owner:me@x.test', 'why') RETURNING id");

    private async Task<long> ModelAsync(char key)
    {
        await StoreKit.ExecuteAsync(server.Owner, TrainingSet, Ct);
        return await ScalarAsync(server.Owner, Model(key));
    }

    private static async Task<List<string>> ColumnsAsync(string connectionString, string table, string privilege)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand($"SELECT column_name FROM information_schema.columns WHERE table_schema = 'research' AND table_name = '{table}' AND has_column_privilege('xd_app', 'research.{table}', column_name, '{privilege}') ORDER BY 1", connection);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public async Task The_runtime_role_has_no_table_level_update_delete_or_truncate_on_the_three_tables_and_the_proposal_is_inserted_and_updated_by_column()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        foreach (var name in new[] { "ml_switch_settings", "ml_monitor_checks", "rollback_proposals" })
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", name != "rollback_proposals"), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", "research." + name);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, $"{privilege} on {name}");
            }
        }

        (await ColumnsAsync(server.Owner, "rollback_proposals", "INSERT")).ShouldBe(["cells", "limit_value", "model_id", "previous_champion_id", "previous_qualifies", "proposed_on", "settings_hash", "text", "trigger", "value"]);
        (await ColumnsAsync(server.Owner, "rollback_proposals", "UPDATE")).ShouldBe(["actor", "auto_setting_id", "decided_at", "state"]);
    }

    [Fact]
    public async Task As_xd_app_the_switch_and_the_evaluations_are_insert_only_the_id_follows_commit_order_and_only_an_owner_names_a_known_switch()
    {
        var model = await ModelAsync('1');
        var first = await SwitchAsync("ml.auto_rollback", true);
        var second = await SwitchAsync("ml.auto_rollback", false);

        second.ShouldBeGreaterThan(first, "the id follows commit order");
        (await SqlStateAsync(server.App, "INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.auto_rollback', true, 'auto', 'why')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.auto_rollback', true, 'owner:me@x.test', '  ')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, "INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.other', true, 'owner:me@x.test', 'why')")).ShouldBe(PostgresErrorCodes.CheckViolation, "only the three switches");
        foreach (var statement in new[]
        {
            "UPDATE research.ml_switch_settings SET enabled = true", "DELETE FROM research.ml_switch_settings", "TRUNCATE research.ml_switch_settings",
            "UPDATE research.ml_monitor_checks SET settings_hash = 'x'", "DELETE FROM research.ml_monitor_checks", "TRUNCATE research.ml_monitor_checks",
        })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        var check = $"INSERT INTO research.ml_monitor_checks (model_id, checked_on, settings_hash, verdicts) VALUES ({model}, '2026-11-06', '{{0}}', '[]')";
        (await SqlStateAsync(server.App, string.Format(System.Globalization.CultureInfo.InvariantCulture, check, Hash))).ShouldBeNull();
        (await SqlStateAsync(server.App, string.Format(System.Globalization.CultureInfo.InvariantCulture, check, Hash))).ShouldBe(PostgresErrorCodes.UniqueViolation, "one evaluation per model and day");
        (await SqlStateAsync(server.App, string.Format(System.Globalization.CultureInfo.InvariantCulture, check.Replace("2026-11-06", "2026-11-07", StringComparison.Ordinal), "zz"))).ShouldNotBeNull("a bad hash");
    }

    [Fact]
    public async Task A_proposal_is_one_per_model_trigger_and_day_checked_and_the_runtime_role_can_only_decide_it_once()
    {
        var model = await ModelAsync('2');
        var id = await ScalarAsync(server.App, Proposal(model));

        (await SqlStateAsync(server.App, Proposal(model))).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await SqlStateAsync(server.App, Proposal(model, day: "2026-11-07"))).ShouldBeNull();
        (await SqlStateAsync(server.App, Proposal(model, trigger: "other"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, Proposal(model + 9999))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation, "a model that exists");
        (await SqlStateAsync(server.App, Proposal(model, trigger: "band", qualifies: true))).ShouldBe(PostgresErrorCodes.CheckViolation, "a previous champion that qualifies has to exist");
        foreach (var statement in new[] { "UPDATE research.rollback_proposals SET trigger = 'band'", "UPDATE research.rollback_proposals SET cells = '[1]'", "UPDATE research.rollback_proposals SET model_id = 1", "DELETE FROM research.rollback_proposals", "TRUNCATE research.rollback_proposals" })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'owner:me@x.test' WHERE id = {id}")).ShouldBeNull();
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'dismissed', actor = 'owner:me@x.test' WHERE id = {id}")).ShouldBe(PostgresErrorCodes.CheckViolation, "a decided proposal is not changed again");
        (await ScalarAsync(server.App, $"SELECT count(*) FROM research.rollback_proposals WHERE id = {id} AND state = 'applied' AND decided_at IS NOT NULL")).ShouldBe(1);
    }

    [Fact]
    public async Task The_runtime_role_cannot_insert_an_already_decided_proposal()
    {
        var model = await ModelAsync('3');
        var on = await SwitchAsync("ml.auto_rollback", true);
        const string decided = "INSERT INTO research.rollback_proposals (model_id, trigger, text, cells, previous_qualifies, settings_hash, proposed_on, {0}) VALUES ({1}, 'band', 't', '[]', false, '{2}', '2026-11-06', {3})";
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
            (await SqlStateAsync(server.App, string.Format(System.Globalization.CultureInfo.InvariantCulture, decided, columns, model, Hash, values))).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, columns);
        }

        (await ScalarAsync(server.App, "SELECT count(*) FROM research.rollback_proposals WHERE trigger = 'band' AND proposed_on = '2026-11-06' AND model_id = " + model)).ShouldBe(0);
    }

    [Fact]
    public async Task The_database_refuses_an_automatic_decision_without_a_switch_that_is_on_newest_and_recorded_for_the_right_name_or_for_a_dismissal()
    {
        var model = await ModelAsync('4');
        var rollbackOn = await SwitchAsync("ml.auto_rollback", true);
        var promoteOn = await SwitchAsync("ml.auto_promote", true);
        var a = await ScalarAsync(server.App, Proposal(model, "calibration"));
        var b = await ScalarAsync(server.App, Proposal(model, "band"));

        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'auto' WHERE id = {a}")).ShouldBe(PostgresErrorCodes.CheckViolation, "an automatic decision records the switch row");
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'owner:me@x.test', auto_setting_id = {rollbackOn} WHERE id = {a}")).ShouldBe(PostgresErrorCodes.CheckViolation, "an owner decision names no switch row");
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'dismissed', actor = 'auto', auto_setting_id = {rollbackOn} WHERE id = {a}")).ShouldBe(PostgresErrorCodes.CheckViolation, "auto never dismisses");
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {promoteOn} WHERE id = {a}")).ShouldBe(PostgresErrorCodes.CheckViolation, "the row of another switch is no authority for a rollback");
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {rollbackOn + 9999} WHERE id = {a}")).ShouldNotBeNull("no such switch row");
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {rollbackOn} WHERE id = {a}")).ShouldBeNull("the newest ml.auto_rollback row is ON and is the one recorded");

        var off = await SwitchAsync("ml.auto_rollback", false);
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {rollbackOn} WHERE id = {b}")).ShouldBe(PostgresErrorCodes.CheckViolation, "the switch was turned OFF since");
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {off} WHERE id = {b}")).ShouldBe(PostgresErrorCodes.CheckViolation, "the recorded row is OFF");
        var again = await SwitchAsync("ml.auto_rollback", true);
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {rollbackOn} WHERE id = {b}")).ShouldBe(PostgresErrorCodes.CheckViolation, "the recorded row is not the newest");
        (await SqlStateAsync(server.App, $"UPDATE research.rollback_proposals SET state = 'applied', actor = 'auto', auto_setting_id = {again} WHERE id = {b}")).ShouldBeNull();
    }
}
