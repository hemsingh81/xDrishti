using Npgsql;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// ADR 0174 (P12-07 criterion 13, reduced): the owner's switch rows as the runtime role. The trigger stamps the time and the hash, carries the values over, refuses auto_live ON and an early raise, and the record is insert-only for every role;
/// the four database checks that read the global switch read the row in force, so a raise waiting for its cooling-off is never read early.
/// </summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecSettingsDatabaseTests(ExecDatabase db)
{
    private const string Owner = "owner:hem@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Current(string column) => $"(SELECT {column} FROM execution.settings WHERE effective_from <= clock_timestamp() ORDER BY id DESC LIMIT 1)";

    [Fact]
    public async Task A_switch_off_by_the_runtime_role_is_in_force_at_once_with_the_values_carried_over_and_a_stamped_hash()
    {
        var before = (string)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT COALESCE({Current("\"values\"::text")}, '{{}}')"))!;

        var id = (long)(await ExecDatabase.ScalarAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason, \"values\", effective_from) VALUES (false, '{Owner}', 'calmer', '{{\"exec.max_order_qty\": \"1\"}}', now() + interval '9 days') RETURNING id"))!;

        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT effective_from = changed_at FROM execution.settings WHERE id = {id}"))!).ShouldBeTrue("a lowering is stamped in force at once");
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT \"values\"::text FROM execution.settings WHERE id = {id}")).ShouldBe(before, "the route cannot change the values");
        ((string)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT snapshot_hash FROM execution.settings WHERE id = {id}"))!).ShouldMatch("^[0-9a-f]{64}$");
        (await ExecDatabase.ScalarAsync(db.Owner, Current("id"))).ShouldBe(id);
    }

    [Fact]
    public async Task Switching_execution_on_is_refused_before_09_00_of_the_next_day_and_a_later_raise_waits_without_being_read_early()
    {
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (false, '{Owner}', 'start off')");

        var early = await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (true, '{Owner}', 'on now')");
        var id = (long)(await ExecDatabase.ScalarAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason, effective_from) VALUES (true, '{Owner}', 'on later', now() + interval '30 days') RETURNING id"))!;

        early.ShouldBe("23514");
        (await ExecDatabase.ScalarAsync(db.Owner, Current("execution_enabled"))).ShouldBe(false);
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT effective_from > changed_at + interval '29 days' FROM execution.settings WHERE id = {id}"))!).ShouldBeTrue();
    }

    [Fact]
    public async Task The_runtime_role_can_never_store_auto_live_on_and_needs_an_owner_actor_and_a_reason()
    {
        foreach (var statement in new[]
        {
            $"INSERT INTO execution.settings (execution_enabled, auto_live_enabled, actor, reason, effective_from) VALUES (true, true, '{Owner}', 'x', now() + interval '30 days')",
            $"INSERT INTO execution.settings (execution_enabled, auto_live_enabled, actor, reason) VALUES (false, true, '{Owner}', 'x')",
            "INSERT INTO execution.settings (actor, reason) VALUES ('owner:', 'x')",
            $"INSERT INTO execution.settings (actor, reason) VALUES ('{Owner}', '   ')",
        })
        {
            (await ExecDatabase.SqlStateAsync(db.App, statement)).ShouldBe("23514", statement);
        }

        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.settings (actor, reason) VALUES ('{Owner}', 'x')")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_settings_record_is_insert_only_for_every_role_including_the_owner()
    {
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.settings (actor, reason) VALUES ('{Owner}', 'row')");

        (await ExecDatabase.SqlStateAsync(db.Owner, "UPDATE execution.settings SET reason = 'changed'")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Owner, "DELETE FROM execution.settings")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Owner, "TRUNCATE execution.settings CASCADE")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.App, "UPDATE execution.settings SET reason = 'changed'")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "DELETE FROM execution.settings")).ShouldBe("42501");
    }

    [Theory]
    [InlineData("execution.check_promotion_request()")]
    [InlineData("execution.check_mode_trail()")]
    [InlineData("execution.check_ticket_outcome()")]
    [InlineData("execution.check_ledger_entry()")]
    public async Task Every_database_check_of_the_global_switch_reads_the_row_in_force(string function)
    {
        var text = (string)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT pg_get_functiondef('{function}'::regprocedure)"))!;

        text.ShouldContain("effective_from <= clock_timestamp() ORDER BY");
        text.ShouldNotMatch(@"FROM execution\.settings [sx] ORDER BY");
    }

    [Fact]
    public async Task A_promotion_request_is_refused_while_switching_execution_on_still_waits_and_accepted_once_a_row_in_force_is_on()
    {
        var account = Guid.NewGuid();
        await ExecDatabase.ExecuteAsync(
            db.Owner,
            $"""
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES ('{account}', 's-{account:N}', 'Settings {account:N}', 'Dhan', '{Random.Shared.NextInt64(2_000_000_000, 9_000_000_000)}', current_date, '#112233', now(), now(), false, false, false, 0);
            INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments)
            VALUES ('{account}', true, 'ManualLive', 'Fixed', 100000, true, false, 1, 0, 0)
            """);
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (false, '{Owner}', 'off')");
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason, effective_from) VALUES (true, '{Owner}', 'on later', now() + interval '30 days')");
        string Request() =>
            $"INSERT INTO execution.promotion_requests (id, account_id, from_mode, to_mode, requested_by, reason, eligible_from, expires_at, evidence, evidence_hash, policy_version) VALUES (gen_random_uuid(), '{account}', 'manual_live', 'assisted', '{Owner}', 'r', now() + interval '48 hours', now() + interval '5 days', '{{}}', repeat('a', 64), 1)";

        var waiting = await MessageAsync(db.App, Request());
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (true, '{Owner}', 'fixture: on now')");
        var inForce = await MessageAsync(db.App, Request());

        waiting.ShouldContain("switch is OFF");
        inForce.ShouldBeEmpty();
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (false, '{Owner}', 'back off')");
    }

    [Fact]
    public async Task A_temporary_table_named_pg_class_cannot_make_the_runtime_role_look_like_the_owner()
    {
        // Arrange the state the assertions assume: the switch is OFF, so an immediate switch-on is the refused case whatever test ran before (the fixture database is shared).
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (false, '{Owner}', 'precondition: off')");
        await using var c = new NpgsqlConnection(db.App);
        await c.OpenAsync(Ct);
        await RunAsync(c, "CREATE TEMP TABLE pg_class (oid oid, relowner oid); INSERT INTO pg_class VALUES ('execution.settings'::regclass::oid, (SELECT oid FROM pg_catalog.pg_roles WHERE rolname = session_user))");
        (await ScalarOnAsync(c, "SELECT pg_get_userbyid(c.relowner) FROM pg_class c WHERE c.oid = 'execution.settings'::regclass")).ShouldBe("xd_app", "negative control: the shadow is what an unpinned lookup would read");

        var autoLive = await StateOnAsync(c, $"INSERT INTO execution.settings (execution_enabled, auto_live_enabled, actor, reason, effective_from) VALUES (true, true, '{Owner}', 'x', now() + interval '30 days')");
        var early = await StateOnAsync(c, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (true, '{Owner}', 'on now')");
        var id = (long)(await ScalarOnAsync(c, $"INSERT INTO execution.settings (execution_enabled, actor, reason, changed_at, effective_from) VALUES (false, '{Owner}', 'back-dated', now() - interval '10 days', now() - interval '10 days') RETURNING id"))!;

        autoLive.ShouldBe("23514 execution.settings_refused: auto_live_enabled stays OFF; no route turns it on", "the trigger itself refused, not only the CHECK");
        early!.ShouldStartWith("23514 execution.settings_refused: switching execution on");
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT changed_at > now() - interval '1 minute' AND effective_from = changed_at FROM execution.settings WHERE id = {id}"))!).ShouldBeTrue("the trigger stamped the database clock");
    }

    [Fact]
    public async Task Every_execution_function_that_names_a_catalog_relation_pins_its_search_path_and_qualifies_the_name()
    {
        await using var c = new NpgsqlConnection(db.Owner);
        await c.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT p.proname, p.prosrc, COALESCE(array_to_string(p.proconfig, ','), '')
              FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
             WHERE n.nspname = 'execution'
            """, c);
        var offenders = new List<string>();
        var named = 0;
        await using (var r = await command.ExecuteReaderAsync(Ct))
        {
            while (await r.ReadAsync(Ct))
            {
                var source = r.GetString(1);
                var catalog = System.Text.RegularExpressions.Regex.Matches(source, @"(?<![\w.])pg_(class|roles|authid|namespace|proc|attribute|tables|trigger|index|database|user|settings|locks|stat_activity)\b");
                if (catalog.Count == 0 && !source.Contains("pg_catalog.pg_", StringComparison.Ordinal))
                {
                    continue;
                }

                named++;
                if (catalog.Count > 0 || !r.GetString(2).Contains("search_path=pg_catalog, pg_temp", StringComparison.Ordinal))
                {
                    offenders.Add(r.GetString(0));
                }
            }
        }

        named.ShouldBeGreaterThan(0, "check_settings names pg_catalog.pg_class");
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rolling_back_while_a_switch_on_waits_writes_an_off_row_first_so_the_newest_row_reads_off()
    {
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (false, '{Owner}', 'off')");
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.settings (execution_enabled, actor, reason, effective_from) VALUES (true, '{Owner}', 'on later', now() + interval '30 days')");

        await ExecDatabase.ExecuteAsync(db.Owner, XDrishti.Infrastructure.Persistence.Migrations.AddExecutionSwitchWrites.DownFailClosed);

        (await ExecDatabase.ScalarAsync(db.Owner, "SELECT execution_enabled FROM execution.settings ORDER BY id DESC LIMIT 1")).ShouldBe(false, "the newest row, which is what the readers take after Down");
        ((bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT effective_from <= clock_timestamp() FROM execution.settings ORDER BY id DESC LIMIT 1"))!).ShouldBeTrue();
    }

    [Fact]
    public async Task The_owner_role_cannot_store_auto_live_on_either()
    {
        (await ExecDatabase.SqlStateAsync(db.Owner, $"INSERT INTO execution.settings (execution_enabled, auto_live_enabled, actor, reason) VALUES (true, true, '{Owner}', 'owner')")).ShouldBe("23514");
    }

    [Theory]
    [InlineData("settings", "execution.settings", "INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (false, 'owner:hem@example.test', 'race') RETURNING id")]
    [InlineData("limits", "execution.limits:00000000-0000-0000-0000-0000000000aa", "INSERT INTO execution.limits (account_id, actor, reason, effective_from, snapshot_hash) VALUES ('00000000-0000-0000-0000-0000000000aa', 'owner:hem@example.test', 'race', now(), repeat('a', 64)) RETURNING id")]
    public async Task A_row_draws_its_id_only_after_the_lock_so_id_order_is_commit_order(string table, string lockKey, string insert)
    {
        await using var holder = new NpgsqlConnection(db.Owner);
        await holder.OpenAsync(Ct);
        await using var tx = await holder.BeginTransactionAsync(Ct);
        await using (var take = new NpgsqlCommand($"SELECT pg_advisory_xact_lock(hashtextextended('{lockKey}', 0))", holder, tx))
        {
            await take.ExecuteNonQueryAsync(Ct);
        }

        var waiting = ExecDatabase.ScalarAsync(db.App, insert);
        var observed = false;
        for (var i = 0; i < 20_000 && !observed && !waiting.IsCompleted; i++)
        {
            // Spin on the catalog (no sleep) until the insert waits for THIS test's key: an advisory lock on a bigint key is classid (high half) and objid (low half) with objsubid 1.
            observed = (long)(await ExecDatabase.ScalarAsync(
                db.Owner,
                $"SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND objsubid = 1 AND ((classid::bigint << 32) | objid::bigint) = hashtextextended('{lockKey}', 0)"))! > 0;
        }

        observed.ShouldBeTrue("the insert never waited for the lock, so the test proves nothing");
        var drawnMeanwhile = (long)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT nextval(pg_get_serial_sequence('execution.{table}', 'id'))"))!;
        await tx.CommitAsync(Ct);
        var id = (long)(await waiting)!;

        id.ShouldBeGreaterThan(drawnMeanwhile, $"{table}: the id is drawn after the lock");
    }

    [Theory]
    [InlineData("check_settings", true)]
    [InlineData("check_limits", true)]
    [InlineData("check_mode_trail", true)]
    [InlineData("check_ledger_entry", false)]
    public async Task Each_trigger_that_judges_against_a_row_read_after_its_lock_is_pinned_read_committed_only_and_draws_ids_with_qualified_calls(string function, bool drawsId)
    {
        var source = (string)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT prosrc FROM pg_proc WHERE oid = 'execution.{function}()'::regprocedure"))!;
        var config = (string)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT COALESCE(array_to_string(proconfig, ','), '') FROM pg_proc WHERE oid = 'execution.{function}()'::regprocedure"))!;

        config.ShouldContain("search_path=pg_catalog, pg_temp");
        source.ShouldContain("pg_catalog.current_setting('transaction_isolation') <> 'read committed'");
        source.ShouldNotMatch(@"(?<![\w.])(nextval|pg_get_serial_sequence|pg_advisory_xact_lock|hashtextextended|current_setting)\(");
        if (drawsId)
        {
            source.ShouldContain("NEW.id := pg_catalog.nextval(pg_catalog.pg_get_serial_sequence(");
        }
    }

    [Fact]
    public async Task At_repeatable_read_a_stale_on_row_cannot_switch_execution_back_on_and_a_plain_raise_is_refused_too()
    {
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (true, '{Owner}', 'fixture: on in force')");
        await using var app = new NpgsqlConnection(db.App);
        await app.OpenAsync(Ct);
        await RunAsync(app, "BEGIN ISOLATION LEVEL REPEATABLE READ; SELECT 1");
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (false, '{Owner}', 'fixture: switched off meanwhile')");

        var onAgain = await StateOnAsync(app, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES (true, '{Owner}', 'on again')");
        await RunAsync(app, "ROLLBACK; BEGIN ISOLATION LEVEL SERIALIZABLE");
        var raise = await StateOnAsync(app, $"INSERT INTO execution.settings (execution_enabled, actor, reason, effective_from) VALUES (true, '{Owner}', 'on later', now() + interval '30 days')");
        await RunAsync(app, "ROLLBACK");

        onAgain!.ShouldStartWith("23514 execution.isolation_refused");
        raise!.ShouldStartWith("23514 execution.isolation_refused");
        (await ExecDatabase.ScalarAsync(db.Owner, Current("execution_enabled"))).ShouldBe(false);
    }

    [Fact]
    public async Task At_repeatable_read_a_limits_sheet_and_a_mode_trail_row_are_refused_and_at_read_committed_the_same_rows_are_accepted()
    {
        var account = Guid.NewGuid();
        await ExecDatabase.ExecuteAsync(
            db.Owner,
            $"""
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES ('{account}', 'r-{account:N}', 'Isolation {account:N}', 'Dhan', '{Random.Shared.NextInt64(2_000_000_000, 9_000_000_000)}', current_date, '#112233', now(), now(), false, false, false, 0);
            INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments)
            VALUES ('{account}', true, 'ManualLive', 'Fixed', 100000, true, false, 1, 0, 0)
            """);
        var sheet = $"INSERT INTO execution.limits (account_id, actor, reason, effective_from, snapshot_hash) VALUES ('{account}', '{Owner}', 'empty sheet', now(), repeat('a', 64))";
        var demotion = $"INSERT INTO execution.mode_trail (account_id, from_mode, to_mode, kind, actor, reason, gate_snapshot, gate_hash, effective_from) VALUES ('{account}', 'manual_live', 'paper', 'owner_demotion', '{Owner}', 'down', '{{}}', repeat('b', 64), now())";
        await using var app = new NpgsqlConnection(db.App);
        await app.OpenAsync(Ct);

        await RunAsync(app, "BEGIN ISOLATION LEVEL REPEATABLE READ");
        var sheetRepeatable = await StateOnAsync(app, sheet);
        await RunAsync(app, "ROLLBACK; BEGIN ISOLATION LEVEL REPEATABLE READ");
        var demotionRepeatable = await StateOnAsync(app, demotion);
        await RunAsync(app, "ROLLBACK");
        var sheetCommitted = await StateOnAsync(app, sheet);
        var demotionCommitted = await StateOnAsync(app, demotion);

        sheetRepeatable!.ShouldStartWith("23514 execution.isolation_refused");
        demotionRepeatable!.ShouldStartWith("23514 execution.isolation_refused");
        sheetCommitted.ShouldBeNull();
        demotionCommitted.ShouldBeNull();
    }

    private static async Task RunAsync(NpgsqlConnection c, string sql)
    {
        await using var command = new NpgsqlCommand(sql, c);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<object?> ScalarOnAsync(NpgsqlConnection c, string sql)
    {
        await using var command = new NpgsqlCommand(sql, c);
        return await command.ExecuteScalarAsync(Ct);
    }

    private static async Task<string?> StateOnAsync(NpgsqlConnection c, string sql)
    {
        try
        {
            await RunAsync(c, sql);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState + " " + failure.MessageText;
        }
    }

    private static async Task<string> MessageAsync(string connection, string sql)
    {
        try
        {
            await ExecDatabase.ExecuteAsync(connection, sql);
            return string.Empty;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState + " " + failure.MessageText;
        }
    }
}
