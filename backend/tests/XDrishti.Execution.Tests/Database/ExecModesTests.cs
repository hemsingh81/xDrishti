using Npgsql;
using XDrishti.Domain.Execution;
using XDrishti.Execution.Application.Modes;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// P12-07 criteria 1, 4 to 8 on a real database, as the real roles: the trail and the requests are insert-only, a promotion needs a matching, newest, unused request inside its window and the global switch ON, the owner writes promotions and owner
/// demotions (xd_app), the execution role writes system demotions only (xd_exec), nothing but a lower rung can be written by the system, and two simultaneous changes serialise so exactly one wins.
/// </summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecModesTests(ExecDatabase db)
{
    private const string Owner = "owner:hem@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> AccountAsync(ExecDatabase db, string mode = "ManualLive")
    {
        var id = Guid.NewGuid();
        var client = Random.Shared.NextInt64(2_000_000_000, 9_000_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ExecDatabase.ExecuteAsync(
            db.Owner,
            $"""
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES ('{id}', 'm-{id:N}', 'Mode {id:N}', 'Dhan', '{client}', current_date, '#112233', now(), now(), false, false, false, 0);
            INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments)
            VALUES ('{id}', true, '{mode}', 'Fixed', 100000, true, false, 1, 0, 0)
            """);
        return id;
    }

    private Task SwitchAsync(bool on) => ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES ({(on ? "true" : "false")}, '{Owner}', 'test')");

    private Task<string> EffectiveAsync(Guid account) => ExecDatabase.ScalarAsync(db.Owner, $"SELECT execution.effective_mode('{account}')").ContinueWith(t => (string)t.Result!, Ct);

    private static string RequestSql(Guid id, Guid account, string from = "manual_live", string to = "assisted", string by = Owner, string eligible = "now() + interval '1 second'", string expires = "now() + interval '1 hour'") =>
        $"INSERT INTO execution.promotion_requests (id, account_id, from_mode, to_mode, requested_by, reason, eligible_from, expires_at, evidence, evidence_hash, policy_version) VALUES ('{id}', '{account}', '{from}', '{to}', '{by}', 'r', {eligible}, {expires}, '{{}}', repeat('a', 64), 1)";

    private static string TrailSql(Guid account, string from, string to, string kind, string actor, Guid? request = null, string? effective = null) =>
        $"INSERT INTO execution.mode_trail (account_id, from_mode, to_mode, kind, actor, reason, request_id, gate_snapshot, gate_hash, effective_from) VALUES ('{account}', '{from}', '{to}', '{kind}', '{actor}', 'r', {(request is null ? "NULL" : $"'{request}'")}, '{{}}', repeat('b', 64), {effective ?? (kind == "promotion" ? "clock_timestamp() + interval '1 second'" : "now()")})";

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

    /// <summary>Writes the promotion and waits out the second it needs before it is in force (a promotion may not take effect at the moment it is written).</summary>
    private async Task PromoteAsync(Guid account, Guid request)
    {
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "assisted", "promotion", Owner, request))).ShouldBeEmpty();
        await Task.Delay(TimeSpan.FromMilliseconds(1300), Ct);
    }

    /// <summary>The floor trigger forbids a window of seconds, which is what a test needs to get inside it: the fixture alone switches that one trigger off for its insert.</summary>
    private async Task InsertRequestPastFloorAsync(string sql)
    {
        await ExecDatabase.ExecuteAsync(db.Owner, "ALTER TABLE execution.promotion_requests DISABLE TRIGGER trg_exec_promotion_requests_check");
        try
        {
            await ExecDatabase.ExecuteAsync(db.Owner, sql);
        }
        finally
        {
            await ExecDatabase.ExecuteAsync(db.Owner, "ALTER TABLE execution.promotion_requests ENABLE TRIGGER trg_exec_promotion_requests_check");
        }
    }

    /// <summary>A request that is already inside its window, made by waiting out the one-second cooling-off the trigger insists on.</summary>
    private async Task<Guid> OpenRequestAsync(Guid account)
    {
        var id = Guid.NewGuid();
        await SwitchAsync(true);
        await InsertRequestPastFloorAsync(RequestSql(id, account));
        await Task.Delay(TimeSpan.FromMilliseconds(1300), Ct);
        return id;
    }

    [Fact]
    public async Task The_effective_mode_is_the_configured_one_until_a_row_is_in_force_and_never_assisted_on_an_account_configured_paper()
    {
        var live = await AccountAsync(db);
        var paper = await AccountAsync(db, "Paper");

        (await EffectiveAsync(live)).ShouldBe("manual_live");
        (await EffectiveAsync(paper)).ShouldBe("paper");
        (await EffectiveAsync(Guid.NewGuid())).ShouldBe("paper");

        var request = await OpenRequestAsync(live);
        (await MessageAsync(db.App, TrailSql(live, "manual_live", "assisted", "promotion", Owner, request, "now() + interval '1 day'"))).ShouldBeEmpty();
        (await EffectiveAsync(live)).ShouldBe("manual_live"); // takes effect at the next open, not now
        await ExecDatabase.ExecuteAsync(db.Owner, $"ALTER TABLE execution.mode_trail DISABLE TRIGGER trg_exec_mode_trail_insert_only; UPDATE execution.mode_trail SET effective_from = now() - interval '1 minute' WHERE account_id = '{live}'; ALTER TABLE execution.mode_trail ENABLE TRIGGER trg_exec_mode_trail_insert_only");
        (await EffectiveAsync(live)).ShouldBe("assisted");
        await ExecDatabase.ExecuteAsync(db.Owner, $"UPDATE accounts.trading_settings SET mode = 'Paper' WHERE account_id = '{live}'"); // the configuration is a ceiling
        (await EffectiveAsync(live)).ShouldBe("paper");
    }

    [Fact]
    public async Task A_promotion_goes_request_then_cooling_off_then_confirm_and_the_system_can_then_only_lower_it()
    {
        var account = await AccountAsync(db);
        var request = await OpenRequestAsync(account);

        await PromoteAsync(account, request);
        (await EffectiveAsync(account)).ShouldBe("assisted");
        (await MessageAsync(db.Exec, TrailSql(account, "assisted", "assisted", "demotion", "system:mode-guard"))).ShouldNotBeEmpty(); // not lower
        (await MessageAsync(db.Exec, TrailSql(account, "assisted", "paper", "demotion", "system:mode-guard", effective: "now() + interval '30 days'"))).ShouldBeEmpty();
        (await EffectiveAsync(account)).ShouldBe("paper"); // a demotion is at once: the effective time was stamped, not taken from the writer
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT effective_from = changed_at FROM execution.mode_trail WHERE account_id = '{account}' AND kind = 'demotion'")).ShouldBe(true);
    }

    [Fact]
    public async Task A_promotion_cannot_take_effect_immediately_or_in_the_past_and_the_ceiling_is_by_rank()
    {
        var account = await AccountAsync(db);
        var request = await OpenRequestAsync(account);

        (await MessageAsync(db.App, TrailSql(account, "manual_live", "assisted", "promotion", Owner, request, "clock_timestamp() - interval '1 second'"))).ShouldContain("immediately or in the past");
        await PromoteAsync(account, request);
        (await EffectiveAsync(account)).ShouldBe("assisted");
        await ExecDatabase.ExecuteAsync(db.Owner, $"UPDATE accounts.trading_settings SET mode = 'Paper' WHERE account_id = '{account}'");
        (await EffectiveAsync(account)).ShouldBe("paper"); // assisted row, paper configuration

        var live = await AccountAsync(db);
        var second = await OpenRequestAsync(live);
        await PromoteAsync(live, second);
        (await MessageAsync(db.App, TrailSql(live, "assisted", "manual_live", "owner_demotion", Owner))).ShouldBeEmpty();
        (await EffectiveAsync(live)).ShouldBe("manual_live");
        await ExecDatabase.ExecuteAsync(db.Owner, $"UPDATE accounts.trading_settings SET mode = 'Paper' WHERE account_id = '{live}'");
        (await EffectiveAsync(live)).ShouldBe("paper"); // a manual_live row on an account later configured paper
        await ExecDatabase.ExecuteAsync(db.Owner, $"UPDATE accounts.trading_settings SET mode = 'ManualLive' WHERE account_id = '{live}'");
        (await EffectiveAsync(live)).ShouldBe("manual_live"); // an assisted trail on a manual_live configuration is covered above; no trail row reads the configuration
    }

    [Fact]
    public async Task A_request_is_refused_for_a_skip_a_wrong_from_mode_a_non_owner_the_switch_off_and_an_unknown_mode()
    {
        var live = await AccountAsync(db);
        var paper = await AccountAsync(db, "Paper");
        await SwitchAsync(true);

        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), paper, "paper", "assisted"))).ShouldContain("one step up");
        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), live, "paper", "assisted"))).ShouldContain("effective mode");
        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), live, by: "system:x"))).ShouldStartWith("23514");
        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), live, to: "semi_auto"))).ShouldStartWith("23514");
        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), live, to: "auto_live"))).ShouldStartWith("23514");
        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), live, eligible: "now() - interval '1 minute'"))).ShouldContain("shorter than the floor");
        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), live, eligible: "now() + interval '1 hour'", expires: "now() + interval '5 days'"))).ShouldContain("shorter than the floor"); // a buggy writer cannot shorten the window
        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), live, eligible: "now() + interval '46 hours'", expires: "now() + interval '5 days'"))).ShouldContain("shorter than the floor");
        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), live, eligible: "now() + interval '48 hours'", expires: "now() + interval '5 days'"))).ShouldBeEmpty();
        await SwitchAsync(false);
        (await MessageAsync(db.App, RequestSql(Guid.NewGuid(), live))).ShouldContain("switch is OFF");
    }

    [Fact]
    public async Task A_promotion_is_refused_without_a_request_before_the_window_after_it_when_superseded_when_used_twice_or_with_the_switch_off()
    {
        var account = await AccountAsync(db);
        await SwitchAsync(true);
        var early = Guid.NewGuid();
        await InsertRequestPastFloorAsync(RequestSql(early, account, eligible: "now() + interval '1 hour'", expires: "now() + interval '2 hours'"));

        (await MessageAsync(db.App, TrailSql(account, "manual_live", "assisted", "promotion", Owner))).ShouldStartWith("23514"); // no request id: the shape check
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "assisted", "promotion", Owner, Guid.NewGuid()))).ShouldContain("no matching promotion request"); // an invented request id
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "assisted", "promotion", Owner, early))).ShouldContain("cooling-off has not ended");

        var expiring = Guid.NewGuid();
        await InsertRequestPastFloorAsync(RequestSql(expiring, account, expires: "now() + interval '2 seconds'"));
        await Task.Delay(TimeSpan.FromMilliseconds(2500), Ct);
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "assisted", "promotion", Owner, expiring))).ShouldContain("expired");

        var older = await OpenRequestAsync(account);
        var newer = Guid.NewGuid();
        await InsertRequestPastFloorAsync(RequestSql(newer, account));
        await Task.Delay(TimeSpan.FromMilliseconds(1300), Ct);
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "assisted", "promotion", Owner, older))).ShouldContain("newer request exists");

        await SwitchAsync(false);
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "assisted", "promotion", Owner, newer))).ShouldContain("switch is OFF");
        await SwitchAsync(true);
        await PromoteAsync(account, newer);
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "assisted", "promotion", Owner, newer))).ShouldContain("effective mode", Case.Insensitive); // used once: the from mode is no longer effective (the unique index on request_id is the second wall)
    }

    [Fact]
    public async Task Each_writer_may_write_only_its_own_kind_and_a_demotion_must_go_down()
    {
        var account = await AccountAsync(db);
        var request = await OpenRequestAsync(account);

        (await MessageAsync(db.Exec, TrailSql(account, "manual_live", "assisted", "promotion", Owner, request))).ShouldContain("owner act"); // the execution role can insert, but never a promotion
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "paper", "demotion", "system:mode-guard"))).ShouldContain("execution service");
        (await MessageAsync(db.Exec, TrailSql(account, "manual_live", "paper", "owner_demotion", Owner))).ShouldContain("written by the API");
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "paper", "owner_demotion", "system:x"))).ShouldStartWith("23514");
        (await MessageAsync(db.Exec, TrailSql(account, "manual_live", "paper", "demotion", Owner))).ShouldStartWith("23514");
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "manual_live", "owner_demotion", Owner))).ShouldStartWith("23514");
        (await MessageAsync(db.App, TrailSql(account, "paper", "assisted", "owner_demotion", Owner))).ShouldContain("effective mode");
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "paper", "owner_demotion", Owner, request))).ShouldStartWith("23514"); // an owner demotion carries no request
        (await EffectiveAsync(account)).ShouldBe("manual_live");
        (await MessageAsync(db.App, TrailSql(account, "manual_live", "paper", "owner_demotion", Owner))).ShouldBeEmpty();
        (await EffectiveAsync(account)).ShouldBe("paper");
    }

    [Fact]
    public async Task Both_tables_refuse_update_delete_and_truncate_even_to_the_owner()
    {
        var account = await AccountAsync(db);
        var request = await OpenRequestAsync(account);
        await PromoteAsync(account, request);

        foreach (var statement in new[]
        {
            $"UPDATE execution.mode_trail SET reason = 'x' WHERE account_id = '{account}'",
            $"DELETE FROM execution.mode_trail WHERE account_id = '{account}'",
            "TRUNCATE execution.mode_trail CASCADE",
            $"UPDATE execution.promotion_requests SET reason = 'x' WHERE id = '{request}'",
            $"DELETE FROM execution.promotion_requests WHERE id = '{request}'",
            "TRUNCATE execution.promotion_requests CASCADE",
        })
        {
            (await ExecDatabase.SqlStateAsync(db.Owner, statement)).ShouldBe("23514", statement);
        }
    }

    [Fact]
    public async Task Two_changes_at_the_same_moment_serialise_so_exactly_one_wins()
    {
        var account = await AccountAsync(db);
        var request = await OpenRequestAsync(account);
        await PromoteAsync(account, request);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => MessageAsync(db.App, TrailSql(account, "assisted", i % 2 == 0 ? "paper" : "manual_live", "owner_demotion", Owner))));

        attempts.Count(a => a.Length == 0).ShouldBe(1);
        attempts.Where(a => a.Length > 0).ShouldAllBe(a => a.Contains("effective mode", StringComparison.Ordinal) || a.Contains("lower", StringComparison.Ordinal));
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT count(*) FROM execution.mode_trail WHERE account_id = '{account}'")).ShouldBe(2L);
    }

    [Fact]
    public async Task The_execution_service_reads_the_effective_mode_through_the_source_and_its_guard_lowers_once_and_can_never_raise()
    {
        var account = await AccountAsync(db);
        var request = await OpenRequestAsync(account);
        await PromoteAsync(account, request);
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var trail = new PgModeTrail(data);
        var source = new ModeTrailSource(trail);

        var before = await source.AnswerAsync(account, Ct);
        var guard = new ModeGuard(trail);
        var healthy = await guard.RunAsync(account, new DemotionFacts(true, false), Ct);
        var first = await guard.RunAsync(account, new DemotionFacts(false, false), Ct);
        var second = await guard.RunAsync(account, new DemotionFacts(false, false), Ct);
        var after = await source.AnswerAsync(account, Ct);

        before!.EffectiveMode.ShouldBe(XDrishti.Execution.Domain.ExecMode.Assisted);
        (healthy, second).ShouldBe((null, null));
        first!.Code.ShouldBe("role_off");
        after!.EffectiveMode.ShouldBe(XDrishti.Execution.Domain.ExecMode.Paper);
        (await trail.DemoteAsync(account, LadderMode.Paper, LadderMode.Assisted, "raise", Ct)).ShouldBeFalse(); // the trigger refuses a row that is not lower
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT actor FROM execution.mode_trail WHERE account_id = '{account}' AND kind = 'demotion'")).ShouldBe("system:mode-guard");
        (await new ModeTrailSource(trail).AnswerAsync(Guid.NewGuid(), Ct))!.EffectiveMode.ShouldBe(XDrishti.Execution.Domain.ExecMode.Paper);
    }
}
