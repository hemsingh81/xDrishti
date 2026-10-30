using Npgsql;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// Criterion 10 of P12-01 (P12-01b): what the two runtime roles may do to the execution record, proved from the catalog over EVERY table of the schema (a table added later without a decision fails here, and so does a migration that re-grants the schema to
/// <c>xd_app</c> without repeating the revokes) and by real statements as the roles. Runs on a dedicated server where <c>xd_app</c> has the default privileges of <c>deploy/db/init/10-roles.sh</c>.
/// </summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecGrantsTests(ExecDatabase db)
{
    private static readonly string[] Privileges = ["SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE", "REFERENCES", "TRIGGER"];

    private static readonly Dictionary<string, string[]> ExecExpected = new()
    {
        ["events"] = ["INSERT", "SELECT"],
        ["intents"] = ["INSERT", "SELECT"],
        ["fills"] = ["INSERT", "SELECT"],
        ["command_results"] = ["INSERT", "SELECT"],
        ["legs"] = ["INSERT", "SELECT", "UPDATE"],
        ["intent_state"] = ["INSERT", "SELECT", "UPDATE"],
        ["positions"] = ["INSERT", "SELECT", "UPDATE"],
        ["broker_calls"] = ["INSERT", "SELECT"],
        ["check_runs"] = ["INSERT", "SELECT"],
        ["check_results"] = ["INSERT", "SELECT"],
        ["check_confirmations"] = ["INSERT", "SELECT"],
        ["alarms"] = ["INSERT", "SELECT"],
        ["alarm_clears"] = ["INSERT", "SELECT"],
        ["halts"] = ["INSERT", "SELECT"],
        ["halt_releases"] = ["SELECT"],
        ["halt_auto_clears"] = ["INSERT", "SELECT"],
        ["day_marks"] = ["INSERT", "SELECT"],
        ["drills"] = ["INSERT", "SELECT"],
        ["kill_switch_log"] = ["INSERT", "SELECT"],
        ["recon_runs"] = ["INSERT", "SELECT"],
        ["recon_breaks"] = ["INSERT", "SELECT"],
        ["break_resolutions"] = ["SELECT"],
        ["mode_trail"] = ["INSERT", "SELECT"], // system demotions only; the trigger refuses the rest (P12-07)
        ["promotion_requests"] = ["SELECT"],
        ["readiness_checks"] = ["INSERT", "SELECT"], // the readiness probe (P12-11)
        ["day_states"] = ["INSERT", "SELECT"], // the 09:10:30 mark (P12-11)
        ["ip_attestations"] = ["SELECT"], // the owner's claim, written by the API (P12-11)
        ["confirm_tickets"] = ["INSERT", "SELECT"], // the issuer of the confirm ticket (P12-08b)
        ["ticket_outcomes"] = ["INSERT", "SELECT"], // expired and refused only; the trigger refuses the owner's two outcomes from this role (P12-08b)
        ["day_unlocks"] = ["SELECT"], // the owner's unlock, written by the API (P12-08b)
        ["limits"] = ["SELECT"], // the owner's limits sheet, written by the API (P12-09)
        ["ledger_entries"] = ["INSERT", "SELECT"], // one row per system-sent entry; the trigger judges it against the sheet in force (P12-09)
        ["action_log"] = ["INSERT", "SELECT"], // one row per automatic action and refusal (P12-09)
        ["commands"] = ["SELECT"],
        ["settings"] = ["SELECT"],
    };

    private static readonly Dictionary<string, string[]> AppExpected = ExecExpected.ToDictionary(e => e.Key, e => e.Key is "commands" or "halt_releases" or "break_resolutions" or "mode_trail" or "promotion_requests" or "ip_attestations" or "ticket_outcomes" or "day_unlocks" or "limits" ? new[] { "INSERT", "SELECT" } : new[] { "SELECT" });

    private async Task<List<string>> TablesAsync()
    {
        await using var c = new NpgsqlConnection(db.Owner);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand("SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'execution' AND c.relkind IN ('r', 'p', 'v', 'm', 'f') ORDER BY 1", c);
        var names = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
        {
            names.Add(r.GetString(0));
        }

        return names;
    }

    private async Task<string[]> HeldAsync(string role, string table)
    {
        var held = new List<string>();
        foreach (var p in Privileges)
        {
            if ((bool)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT has_table_privilege('{role}', 'execution.{table}', '{p}')"))!)
            {
                held.Add(p);
            }
        }

        return [.. held.Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task Every_table_of_the_schema_is_decided_for_both_roles_and_no_other_table_exists()
    {
        var tables = await TablesAsync();

        tables.ShouldBe([.. ExecExpected.Keys.Order(StringComparer.Ordinal)]);
        foreach (var table in tables)
        {
            (await HeldAsync("xd_exec", table)).ShouldBe(ExecExpected[table], $"xd_exec on {table}");
            (await HeldAsync("xd_app", table)).ShouldBe(AppExpected[table], $"xd_app on {table}");
        }
    }

    [Fact]
    public async Task No_role_can_use_a_sequence_it_does_not_need_and_nobody_else_can_enter_the_schema()
    {
        foreach (var sequence in new[] { "events_id_seq", "intents_id_seq", "legs_id_seq", "fills_id_seq", "settings_id_seq", "broker_calls_id_seq", "halts_id_seq", "drills_id_seq", "kill_switch_log_id_seq", "recon_runs_id_seq", "recon_breaks_id_seq", "readiness_checks_id_seq", "day_states_id_seq", "ledger_entries_id_seq", "action_log_id_seq" })
        {
            ((bool)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT has_sequence_privilege('xd_app', 'execution.{sequence}', 'USAGE')"))!).ShouldBeFalse(sequence);
            ((bool)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT has_sequence_privilege('xd_exec', 'execution.{sequence}', 'USAGE')"))!).ShouldBeTrue(sequence);
        }

        // The mode trail is written by both roles (owner acts by xd_app, system demotions by xd_exec; the trigger decides which kind each may write), so both need its sequence (P12-07).
        foreach (var role in new[] { "xd_app", "xd_exec" })
        {
            ((bool)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT has_sequence_privilege('{role}', 'execution.mode_trail_id_seq', 'USAGE')"))!).ShouldBeTrue(role);
        }

        // The attestation is the owner's act: the API writes it, the execution service only reads it (P12-11).
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT has_sequence_privilege('xd_app', 'execution.ip_attestations_id_seq', 'USAGE')"))!).ShouldBeTrue();
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT has_sequence_privilege('xd_exec', 'execution.ip_attestations_id_seq', 'USAGE')"))!).ShouldBeFalse();

        // The day unlock is the owner's act: the API writes it, the execution service only reads it (P12-08b).
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT has_sequence_privilege('xd_app', 'execution.day_unlocks_id_seq', 'USAGE')"))!).ShouldBeTrue();
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT has_sequence_privilege('xd_exec', 'execution.day_unlocks_id_seq', 'USAGE')"))!).ShouldBeFalse();

        // The limits sheet is the owner's act: the API writes it, the execution service only reads it (P12-09).
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT has_sequence_privilege('xd_app', 'execution.limits_id_seq', 'USAGE')"))!).ShouldBeTrue();
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT has_sequence_privilege('xd_exec', 'execution.limits_id_seq', 'USAGE')"))!).ShouldBeFalse();

        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT has_schema_privilege('xd_mcp_read', 'execution', 'USAGE') OR has_schema_privilege('xd_mcp_sql', 'execution', 'USAGE')"))!).ShouldBeFalse();
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT has_schema_privilege('public', 'execution', 'USAGE')"))!).ShouldBeFalse();
    }

    [Fact]
    public async Task The_runtime_role_can_read_everything_instruct_by_one_row_and_write_nothing_else()
    {
        const string Event = "INSERT INTO execution.events (intent_id, seq, kind, source, actor) VALUES (1, 1, 'alarm', 'engine', 'x')";
        (await ExecDatabase.SqlStateAsync(db.App, "SELECT count(*) FROM execution.events")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.App, Event)).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "UPDATE execution.intent_state SET state = 'closed'")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "DELETE FROM execution.command_results")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "TRUNCATE execution.commands")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "INSERT INTO execution.command_results (command_id, result, code) VALUES (gen_random_uuid(), 'done', 'x')")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.commands (id, kind, actor) VALUES ('{Guid.NewGuid()}', 'ping', 'system:grants')")).ShouldBeNull();
    }

    [Fact]
    public async Task The_execution_role_inserts_events_and_never_deletes_or_truncates_and_writes_nothing_outside_the_schema_but_its_heartbeat()
    {
        foreach (var table in ExecExpected.Keys)
        {
            (await ExecDatabase.SqlStateAsync(db.Exec, $"DELETE FROM execution.{table}")).ShouldBe("42501", table);
            (await ExecDatabase.SqlStateAsync(db.Exec, $"TRUNCATE execution.{table}")).ShouldBe("42501", table);
        }

        (await ExecDatabase.SqlStateAsync(db.Exec, "UPDATE execution.events SET actor = 'y'")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, "UPDATE execution.intents SET quantity = 1")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO execution.commands (id, kind, actor) VALUES (gen_random_uuid(), 'ping', 'system:x')")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO execution.settings (actor, reason) VALUES ('owner:x', 'y')")).ShouldBe("42501");

        await using var c = new NpgsqlConnection(db.Owner);
        await c.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT n.nspname || '.' || c.relname
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('execution', 'pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_%' AND n.nspname NOT LIKE '_timescaledb%'
              AND (has_table_privilege('xd_exec', c.oid, 'INSERT') OR has_table_privilege('xd_exec', c.oid, 'UPDATE') OR has_table_privilege('xd_exec', c.oid, 'DELETE') OR has_table_privilege('xd_exec', c.oid, 'TRUNCATE'))
            ORDER BY 1
            """, c);
        var writable = new List<string>();
        await using (var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await r.ReadAsync(TestContext.Current.CancellationToken))
            {
                writable.Add(r.GetString(0));
            }
        }

        writable.ShouldBe(["platform.service_heartbeats"]);
        (await ExecDatabase.SqlStateAsync(db.Exec, "DELETE FROM platform.service_heartbeats")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_execution_role_reads_the_plan_and_the_market_but_not_the_vault_the_sessions_or_the_connections()
    {
        foreach (var table in new[] { "trading.plans", "trading.plan_items", "trading.plan_item_allocations", "trading.plan_events", "workflow.approvals", "accounts.broker_accounts", "market.live_quotes", "market.instruments", "market.instrument_broker_ids", "market.trading_days", "portfolio.holdings" })
        {
            (await ExecDatabase.SqlStateAsync(db.Exec, $"SELECT count(*) FROM {table}")).ShouldBeNull(table);
        }

        foreach (var table in new[] { "accounts.broker_secrets", "auth.sessions", "auth.users", "platform.app_settings" })
        {
            (await ExecDatabase.SqlStateAsync(db.Exec, $"SELECT count(*) FROM {table}")).ShouldBe("42501", table);
        }
    }

    [Fact]
    public async Task The_execution_role_reads_three_columns_of_the_connections_and_nothing_of_the_vault()
    {
        // P12-02b (ADR 0158): the token check needs the status and the expiry; the vault holds ciphertext and xd_exec has no key, the sandbox token is a secret file.
        (await ExecDatabase.SqlStateAsync(db.Exec, "SELECT account_id, status, expires_at FROM accounts.broker_connections")).ShouldBeNull();
        foreach (var statement in new[]
        {
            "SELECT * FROM accounts.broker_connections",
            "SELECT broker_user_id FROM accounts.broker_connections",
            "SELECT broker_user_name FROM accounts.broker_connections",
            "SELECT last_error FROM accounts.broker_connections",
            "SELECT ciphertext FROM accounts.broker_secrets",
            "SELECT account_id FROM accounts.broker_secrets",
            "SELECT count(*) FROM accounts.broker_secrets",
            "UPDATE accounts.broker_connections SET status = 'Connected'",
            "INSERT INTO accounts.broker_secrets (account_id, name, ciphertext, key_version, updated_at) VALUES (gen_random_uuid(), 'x', '\\x00', 1, now())",
            "DELETE FROM accounts.broker_connections",
        })
        {
            (await ExecDatabase.SqlStateAsync(db.Exec, statement)).ShouldBe("42501", statement);
        }

        foreach (var (table, privilege) in new[] { ("accounts.broker_secrets", "SELECT"), ("accounts.broker_secrets", "INSERT"), ("accounts.broker_connections", "INSERT"), ("accounts.broker_connections", "UPDATE"), ("accounts.broker_connections", "DELETE") })
        {
            ((bool)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT has_table_privilege('xd_exec', '{table}', '{privilege}')"))!).ShouldBeFalse($"{table} {privilege}");
        }

        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT bool_or(has_column_privilege('xd_exec', 'accounts.broker_secrets', attname, 'SELECT')) FROM pg_attribute WHERE attrelid = 'accounts.broker_secrets'::regclass AND attnum > 0"))!).ShouldBeFalse();
    }
}
