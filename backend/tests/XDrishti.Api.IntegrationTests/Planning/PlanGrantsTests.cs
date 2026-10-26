using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P7-03 (ADR 0083): what the runtime role <c>xd_app</c> may do to the plan record. Runs on the dedicated throw-away server of <see cref="ResearchGrantsTests"/>, where the role has the default privileges of the init script and the chain is migrated by the owner.
/// Every table of <c>trading</c> and <c>workflow</c> is insert-only: a state change is a new row, a decision a new approval, nothing is edited or removed.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PlanGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] RejectedReasons = ["why"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    [Fact]
    public async Task On_every_plan_table_the_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var tables = new NpgsqlCommand("SELECT schemaname || '.' || tablename FROM pg_tables WHERE schemaname IN ('trading', 'workflow') ORDER BY 1", connection);
        var names = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        names.ShouldBe(["trading.fill_grades", "trading.grades", "trading.journal_entries", "trading.journal_fills", "trading.plan_events", "trading.plan_item_allocations", "trading.plan_items", "trading.plan_runs", "trading.plans", "trading.ticket_alerts", "trading.ticket_notice_acks", "trading.ticket_prompts", "workflow.approvals", "workflow.autonomy_settings", "workflow.charge_sheet_events", "workflow.suggestion_seen", "workflow.suggestions"]);
        foreach (var name in names)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", name);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, $"{privilege} on {name}");
            }
        }
    }

    [Fact]
    public async Task On_every_paper_table_the_runtime_role_reads_and_inserts_and_can_neither_change_nor_delete()
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var tables = new NpgsqlCommand("SELECT schemaname || '.' || tablename FROM pg_tables WHERE schemaname = 'paper' ORDER BY 1", connection);
        var names = new List<string>();
        await using (var reader = await tables.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        names.ShouldBe(["paper.capture_runs", "paper.config_changes", "paper.config_snapshots", "paper.entries", "paper.epoch_accounts", "paper.epoch_notes", "paper.epochs", "paper.exits", "paper.skips", "paper.weekly_reviews"]);
        foreach (var name in names)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                await using var check = new NpgsqlCommand("SELECT has_table_privilege('xd_app', @t, @p)", connection);
                check.Parameters.AddWithValue("t", name);
                check.Parameters.AddWithValue("p", privilege);
                ((bool)(await check.ExecuteScalarAsync(Ct))!).ShouldBe(expected, $"{privilege} on {name}");
            }
        }
    }

    [Fact]
    public async Task As_xd_app_inserting_works_and_updating_deleting_and_truncating_fail_with_insufficient_privilege()
    {
        const string plan = "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at, basis_flags_json) VALUES ('grants-probe', '2026-10-13', '2026-10-13 03:35:00+00', 1, repeat('a', 64), 'b', true, 'n', 'l', 'probe', now(), '{}')";

        (await SqlStateAsync(server.App, plan)).ShouldBeNull();
        (await SqlStateAsync(server.App, "INSERT INTO workflow.approvals (plan_id, kind, decision, actor, reason, decided_at) VALUES (1, 'owner', 'approve', 'owner:a', 'r', now())")).ShouldNotBe(PostgresErrorCodes.InsufficientPrivilege, "the privilege to insert an approval is there");
        foreach (var statement in new[]
        {
            "UPDATE trading.plans SET basis = 'x'",
            "DELETE FROM trading.plans",
            "TRUNCATE trading.plans",
            "UPDATE trading.plan_items SET symbol = 'x' WHERE false",
            "DELETE FROM trading.plan_items WHERE false",
            "TRUNCATE trading.plan_items",
            "UPDATE trading.plan_item_allocations SET quantity = 1 WHERE false",
            "DELETE FROM trading.plan_item_allocations WHERE false",
            "UPDATE trading.plan_events SET to_state = 'approved' WHERE false",
            "DELETE FROM trading.plan_events WHERE false",
            "TRUNCATE trading.plan_events",
            "UPDATE trading.plan_runs SET reason = 'x' WHERE false",
            "DELETE FROM trading.plan_runs WHERE false",
            "UPDATE trading.grades SET r = 1 WHERE false",
            "DELETE FROM trading.grades WHERE false",
            "TRUNCATE trading.grades",
            "UPDATE trading.journal_entries SET for_date = current_date WHERE false",
            "DELETE FROM trading.journal_entries WHERE false",
            "TRUNCATE trading.journal_entries",
            "UPDATE trading.journal_fills SET role = 'exit' WHERE false",
            "DELETE FROM trading.journal_fills WHERE false",
            "TRUNCATE trading.journal_fills",
            "UPDATE trading.fill_grades SET r = 1 WHERE false",
            "DELETE FROM trading.fill_grades WHERE false",
            "TRUNCATE trading.fill_grades",
            "UPDATE trading.ticket_alerts SET price = 1 WHERE false",
            "DELETE FROM trading.ticket_alerts WHERE false",
            "TRUNCATE trading.ticket_alerts",
            "UPDATE trading.ticket_prompts SET session = current_date WHERE false",
            "DELETE FROM trading.ticket_prompts WHERE false",
            "TRUNCATE trading.ticket_prompts",
            "UPDATE trading.ticket_notice_acks SET action = 'dismiss' WHERE false",
            "DELETE FROM trading.ticket_notice_acks WHERE false",
            "TRUNCATE trading.ticket_notice_acks",
            "UPDATE workflow.approvals SET decision = 'approve' WHERE false",
            "DELETE FROM workflow.approvals WHERE false",
            "TRUNCATE workflow.approvals",
            "UPDATE workflow.autonomy_settings SET auto_approve_tickets = true WHERE false",
            "DELETE FROM workflow.autonomy_settings WHERE false",
            "TRUNCATE workflow.autonomy_settings",
            "UPDATE workflow.suggestions SET text = 'x' WHERE false",
            "DELETE FROM workflow.suggestions WHERE false",
            "TRUNCATE workflow.suggestions",
            "UPDATE workflow.suggestion_seen SET actor = 'owner:x' WHERE false",
            "DELETE FROM workflow.suggestion_seen WHERE false",
            "TRUNCATE workflow.suggestion_seen",
            "UPDATE workflow.charge_sheet_events SET kind = 'revoke' WHERE false",
            "DELETE FROM workflow.charge_sheet_events WHERE false",
            "TRUNCATE workflow.charge_sheet_events",
        })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        // A suggestion is stored once per plan, kind and subject (P7-09): the second insert is skipped, not an error and not a second row.
        const string suggestion = "INSERT INTO workflow.suggestions (plan_id, kind, subject, text, created_at) SELECT id, 'no_primary_tickets', 'plan', 't', now() FROM trading.plans WHERE plan_key = 'grants-probe' ON CONFLICT (plan_id, kind, subject) DO NOTHING";
        (await SqlStateAsync(server.App, suggestion)).ShouldBeNull();
        (await SqlStateAsync(server.App, suggestion)).ShouldBeNull();
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM workflow.suggestions", Ct)).ShouldBe(1);

        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM trading.plans WHERE plan_key = 'grants-probe'", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task As_xd_app_the_charge_sheet_log_takes_owner_acts_only_and_confirms_only_while_the_capability_is_on()
    {
        var rates = new string('a', 64);
        string Confirm(string actor = "owner:probe@example.test") => $"INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, actor, reason) VALUES ('confirm', 'grants-sheet', '{rates}', '{actor}', 'probe')";
        const string On = "INSERT INTO workflow.charge_sheet_events (kind, actor, reason) VALUES ('gate_on', 'owner:probe@example.test', 'probe')";
        const string Off = "INSERT INTO workflow.charge_sheet_events (kind, actor, reason) VALUES ('gate_off', 'owner:probe@example.test', 'probe')";

        (await SqlStateAsync(server.App, Confirm())).ShouldBe(PostgresErrorCodes.CheckViolation); // no capability row: off
        (await SqlStateAsync(server.App, On)).ShouldBeNull();
        (await SqlStateAsync(server.App, Confirm("cli:xd"))).ShouldBe(PostgresErrorCodes.CheckViolation); // only an owner
        (await SqlStateAsync(server.App, Confirm("rule:autonomy"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, Confirm())).ShouldBeNull();
        (await SqlStateAsync(server.App, "INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, actor, reason) VALUES ('confirm', 'test-sheet', '" + rates + "', 'owner:probe@example.test', 'probe')")).ShouldBe(PostgresErrorCodes.CheckViolation); // a test sheet
        (await SqlStateAsync(server.App, "INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, actor, reason) VALUES ('confirm', 'grants-sheet', 'not-a-hash', 'owner:probe@example.test', 'probe')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.App, Off)).ShouldBeNull();
        (await SqlStateAsync(server.App, Confirm())).ShouldBe(PostgresErrorCodes.CheckViolation); // switched off again
        (await SqlStateAsync(server.App, $"INSERT INTO workflow.charge_sheet_events (kind, sheet_id, rates_sha256, actor, reason) VALUES ('revoke', 'grants-sheet', '{rates}', 'owner:probe@example.test', 'probe')")).ShouldBeNull(); // a revoke needs no capability

        // The database draws the id, the time and the India date of a confirmation: a chosen value is overwritten.
        (await SqlStateAsync(server.App, On)).ShouldBeNull();
        (await SqlStateAsync(server.App, $"INSERT INTO workflow.charge_sheet_events (id, kind, sheet_id, rates_sha256, confirmed_on, actor, reason, created_at) OVERRIDING SYSTEM VALUE VALUES (1, 'confirm', 'grants-sheet', '{rates}', '2000-01-01', 'owner:probe@example.test', 'probe', '2000-01-01')")).ShouldBeNull();
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM workflow.charge_sheet_events WHERE created_at < '2020-01-01' OR confirmed_on < '2020-01-01' OR id = 1", Ct)).ShouldBe(0);
        (await StoreKit.CountAsync(server.App, "SELECT count(*) FROM workflow.charge_sheet_events", Ct)).ShouldBe(6);
    }

    [Fact]
    public async Task As_xd_app_the_plan_trigger_fires_and_cannot_be_replaced_dropped_or_disabled()
    {
        const string Plan = "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES ('grants-trigger', '2026-10-14', '2026-10-14 03:35:00+00', 2, repeat('b', 64), 'b', true, 'n', 'l', 'probe', now())";
        const string Item = "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) SELECT id, @rank, @grp, 's', 'BUY', 'x', 'h', 't', 's', 't', 'v', @reasons, 'c', 1, 'd', 'b', '{}' FROM trading.plans WHERE plan_key = 'grants-trigger'";
        const string Event = "INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, created_at) SELECT plan_id, id, 'proposed', 'lapsed', 'a', 'r', now() FROM trading.plan_items WHERE item_rank = @rank AND plan_id = (SELECT id FROM trading.plans WHERE plan_key = 'grants-trigger')";

        await using var connection = new NpgsqlConnection(server.App);
        await connection.OpenAsync(Ct);

        async Task<string?> RunAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            try
            {
                await command.ExecuteNonQueryAsync(Ct);
                return null;
            }
            catch (PostgresException failure)
            {
                return failure.SqlState;
            }
        }

        (await RunAsync(Plan)).ShouldBeNull();
        (await RunAsync(Item, ("rank", 1), ("grp", "primary"), ("reasons", Array.Empty<string>()))).ShouldBeNull();
        (await RunAsync(Item, ("rank", 2), ("grp", "rejected"), ("reasons", RejectedReasons))).ShouldBeNull();
        (await RunAsync(Event, ("rank", 1))).ShouldBeNull("an event on a ticket is accepted");
        (await RunAsync(Event, ("rank", 2))).ShouldBe(PostgresErrorCodes.CheckViolation, "the trigger refuses an event on a rejected candidate");
        foreach (var statement in new[]
        {
            "CREATE OR REPLACE FUNCTION trading.check_plan_event() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$",
            "DROP TRIGGER trg_plan_events_check ON trading.plan_events",
            "ALTER TABLE trading.plan_events DISABLE TRIGGER trg_plan_events_check",
            "ALTER TABLE workflow.approvals DISABLE TRIGGER trg_approvals_stamp",
            "DROP TRIGGER trg_approvals_stamp ON workflow.approvals",
            "CREATE OR REPLACE FUNCTION workflow.stamp_approval() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END $$",
            "DROP TRIGGER trg_autonomy_settings_stamp ON workflow.autonomy_settings",
            "ALTER TABLE workflow.autonomy_settings DISABLE TRIGGER trg_autonomy_settings_stamp",
        })
        {
            (await RunAsync(statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }
    }

    [Fact]
    public async Task As_xd_app_creating_or_altering_a_plan_object_fails_with_insufficient_privilege()
    {
        foreach (var statement in new[] { "CREATE TABLE trading.x (i integer)", "ALTER TABLE trading.plans ADD COLUMN x integer", "DROP TABLE workflow.approvals", "CREATE TABLE workflow.x (i integer)" })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }
    }

    [Fact]
    public async Task As_xd_app_an_auto_approval_follows_the_newest_settings_row_and_every_other_refusal_holds()
    {
        const string Plan = "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES ('grants-auto', '2099-02-02', '2099-02-02 03:35:00+00', 7, repeat('c', 64), 'b', true, 'n', 'l', 'probe', now())";
        const string Published = "INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, created_at) SELECT id, NULL, 'draft', 'published', 'a', 'r', now() FROM trading.plans WHERE plan_key = 'grants-auto'";
        const string Item = "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) SELECT id, @rank, @grp, 's', 'BUY', 'x', 'h', 't', 's', 't', 'v', @reasons, 'c', 1, 'd', 'b', '{}' FROM trading.plans WHERE plan_key = 'grants-auto'";
        const string Setting = "INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, snapshot_hash, actor, reason, changed_at) VALUES (@on, false, 0.58, 5, 'x', @actor, 'r', now())";
        const string Auto = "INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, cutoff, decided_at, settings_id, settings_hash, policy_version) SELECT p.id, i.id, 'auto', 'approve', @actor, 'r', p.review_cutoff, now(), s.id, s.snapshot_hash, 'v' FROM trading.plans p JOIN trading.plan_items i ON i.plan_id = p.id AND i.item_rank = @rank, (SELECT id, snapshot_hash FROM workflow.autonomy_settings ORDER BY id DESC LIMIT 1) s WHERE p.plan_key = 'grants-auto'";

        await using var connection = new NpgsqlConnection(server.App);
        await connection.OpenAsync(Ct);

        async Task<string?> RunAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            try
            {
                await command.ExecuteNonQueryAsync(Ct);
                return null;
            }
            catch (PostgresException failure)
            {
                return failure.SqlState;
            }
        }

        (await RunAsync(Plan)).ShouldBeNull();
        (await RunAsync(Published)).ShouldBeNull();
        (await RunAsync(Item, ("rank", 1), ("grp", "primary"), ("reasons", Array.Empty<string>()))).ShouldBeNull();
        (await RunAsync(Item, ("rank", 2), ("grp", "reserve"), ("reasons", RejectedReasons))).ShouldBeNull();

        (await RunAsync(Setting, ("on", true), ("actor", "rule:autonomy"))).ShouldBe(PostgresErrorCodes.CheckViolation); // only an owner actor writes a setting
        (await RunAsync(Setting, ("on", true), ("actor", "owner:a"))).ShouldBeNull();
        (await RunAsync(Auto, ("actor", "owner:a"), ("rank", 1))).ShouldBe(PostgresErrorCodes.CheckViolation); // an auto row never carries an owner actor
        (await RunAsync(Auto, ("actor", "rule:autonomy"), ("rank", 2))).ShouldBe(PostgresErrorCodes.CheckViolation); // a reserve item
        (await RunAsync(Auto, ("actor", "rule:autonomy"), ("rank", 1))).ShouldBeNull(); // the newest row has the switch on
        (await RunAsync(Setting, ("on", false), ("actor", "owner:a"))).ShouldBeNull();
        (await RunAsync(Auto, ("actor", "rule:autonomy"), ("rank", 1))).ShouldBe(PostgresErrorCodes.CheckViolation); // now the newest row is off
    }
}
