using Npgsql;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// P12-05 criteria 6, 7 and 11 on a real database, as the real roles: the halt record is insert-only, a halt ends only by an owner release (written by xd_app, checked by the trigger) or an automatic clear of a code that may clear itself (written by xd_exec),
/// a restart (a new store on the same database) finds the halts in force, and the day marks, drills and kill-switch log are insert-only.
/// </summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecHaltsTests(ExecDatabase db)
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 4, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HaltDraft Draft(string code = "owner_button", HaltLevel level = HaltLevel.Cancel, Guid? account = null, bool owner = true, bool kill = false) =>
        new(account is null ? HaltScope.System : HaltScope.Account, account, level, code, owner ? "owner" : "system", owner ? "owner:hem@example.test" : "system:halts", "reason", At, kill);

    private Task<string?> AsAppReleaseAsync(long id, string reason = "reviewed", string by = "owner:hem@example.test") =>
        ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.halt_releases (halt_id, released_by, reason) VALUES ({id}, '{by}', '{reason}')");

    private async Task<string> ReleaseMessageAsync(long id, string reason)
    {
        try
        {
            await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.halt_releases (halt_id, released_by, reason) VALUES ({id}, 'owner:hem@example.test', '{reason}')");
            return string.Empty;
        }
        catch (PostgresException failure)
        {
            return failure.MessageText;
        }
    }

    [Fact]
    public async Task A_halt_is_one_row_a_repeat_does_not_stack_a_stronger_level_is_a_new_row_and_a_new_store_on_the_same_database_finds_it_after_a_restart()
    {
        var account = Guid.NewGuid();
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgHaltStore(data);

        var (first, created) = await store.AppendAsync(Draft("protection_breach", HaltLevel.Entries, account, owner: false), Ct);
        var (again, createdAgain) = await store.AppendAsync(Draft("protection_breach", HaltLevel.Entries, account, owner: false), Ct);
        var (stronger, createdStronger) = await store.AppendAsync(Draft("protection_breach", HaltLevel.Cancel, account, owner: false), Ct);

        (created, createdAgain, createdStronger).ShouldBe((true, false, true));
        again.Id.ShouldBe(first.Id);
        stronger.Id.ShouldNotBe(first.Id);
        await using var afterRestart = NpgsqlDataSource.Create(db.Exec);
        var found = (await new PgHaltStore(afterRestart).ActiveAsync(Ct)).Where(h => h.AccountId == account).ToList();
        found.Select(h => h.Level).Order().ShouldBe([HaltLevel.Entries, HaltLevel.Cancel]);
        found.ShouldAllBe(h => h.Source == "system" && h.Actor == "system:halts");
        (await new HaltService(new PgHaltStore(afterRestart), TimeProvider.System).ReadAsync(Ct)).ShouldBe(HaltReading.Halted);
    }

    [Fact]
    public async Task The_record_refuses_edits_deletes_truncates_a_wrong_source_a_system_halt_with_an_account_and_an_unknown_code_even_to_the_owner()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var (halt, _) = await new PgHaltStore(data).AppendAsync(Draft("daily_loss", HaltLevel.Entries, Guid.NewGuid()), Ct);

        foreach (var statement in new[] { $"UPDATE execution.halts SET reason = 'x' WHERE id = {halt.Id}", $"DELETE FROM execution.halts WHERE id = {halt.Id}", "TRUNCATE execution.halts CASCADE" })
        {
            (await ExecDatabase.SqlStateAsync(db.Owner, statement)).ShouldBe("23514", statement);
        }

        const string Insert = "INSERT INTO execution.halts (at, scope, account_id, level, trigger_code, source, actor, reason) VALUES (now(), '{0}', {1}, '{2}', '{3}', '{4}', '{5}', 'r')";
        var uuid = $"'{Guid.NewGuid()}'";
        (await ExecDatabase.SqlStateAsync(db.Exec, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "system", uuid, "cancel", "owner_button", "owner", "owner:x"))).ShouldBe("23514", "a system halt names no account");
        (await ExecDatabase.SqlStateAsync(db.Exec, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "account", uuid, "cancel", "made_up", "system", "system:x"))).ShouldBe("23514", "unknown code");
        (await ExecDatabase.SqlStateAsync(db.Exec, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "account", uuid, "cancel", "owner_button", "system", "system:x"))).ShouldBe("23514", "the button is an owner act");
        (await ExecDatabase.SqlStateAsync(db.Exec, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "account", uuid, "weaker", "daily_loss", "system", "system:x"))).ShouldBe("23514", "unknown level");
        (await ExecDatabase.SqlStateAsync(db.Exec, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "account", uuid, "cancel", "daily_loss", "owner", "system:x"))).ShouldBe("23514", "owner source needs an owner actor");
        (await ExecDatabase.SqlStateAsync(db.App, string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "account", uuid, "cancel", "daily_loss", "system", "system:x"))).ShouldBe("42501", "the API can not start a halt row");
        (await ExecDatabase.SqlStateAsync(db.Exec, "INSERT INTO execution.halts (at, scope, level, trigger_code, source, actor, reason, broker_kill_switch) VALUES (now(), 'system', 'cancel', 'feed_loss', 'system', 'system:x', 'r', true)")).ShouldBe("23514", "the broker switch is never asked for a code that clears itself");
    }

    [Fact]
    public async Task The_owner_release_is_written_by_the_app_role_ends_the_halt_once_and_the_execution_role_can_not_write_it()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgHaltStore(data);
        var (halt, _) = await store.AppendAsync(Draft("daily_loss", HaltLevel.Entries, Guid.NewGuid()), Ct);

        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.halt_releases (halt_id, released_by, reason) VALUES ({halt.Id}, 'owner:x', 'r')")).ShouldBe("42501");
        (await AsAppReleaseAsync(halt.Id, by: "system:halts")).ShouldBe("23514", "the releaser is an owner");
        (await AsAppReleaseAsync(halt.Id, by: "owner:")).ShouldBe("23514");
        (await AsAppReleaseAsync(halt.Id)).ShouldBeNull();
        (await AsAppReleaseAsync(halt.Id)).ShouldBe("23505", "a halt is released once");
        (await store.ActiveAsync(Ct)).ShouldNotContain(h => h.Id == halt.Id);
        (await ExecDatabase.SqlStateAsync(db.App, $"UPDATE execution.halt_releases SET reason = 'z' WHERE halt_id = {halt.Id}")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Owner, $"UPDATE execution.halt_releases SET reason = 'z' WHERE halt_id = {halt.Id}")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Owner, $"DELETE FROM execution.halt_releases WHERE halt_id = {halt.Id}")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.App, "INSERT INTO execution.halt_releases (halt_id, released_by, reason) VALUES (999999999, 'owner:x', 'r')")).ShouldBe("23514", "no such halt");
    }

    [Fact]
    public async Task A_drawdown_halt_needs_a_review_of_20_characters_a_freeze_is_never_released_and_an_exit_all_only_when_the_accounts_positions_are_flat()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgHaltStore(data);
        var (drawdown, _) = await store.AppendAsync(Draft("drawdown", HaltLevel.Entries, Guid.NewGuid(), owner: false), Ct);
        (await ReleaseMessageAsync(drawdown.Id, "too short")).ShouldContain("drawdown halt needs a written review");
        (await ReleaseMessageAsync(drawdown.Id, "I reviewed the losing week")).ShouldBeEmpty();

        var (freeze, _) = await store.AppendAsync(Draft("reconciliation_break", HaltLevel.Freeze, Guid.NewGuid(), owner: false), Ct);
        (await ReleaseMessageAsync(freeze.Id, "all resolved I think")).ShouldContain("freeze is released only after");

        var account = Guid.NewGuid();
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES ('{account}', 'halt-{account:N}', 'H', 'Dhan', '{Random.Shared.NextInt64(2000000000, 2999999999)}', current_date, '#112233', now(), now(), false, false, false, 0)");
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.positions (account_id, symbol, product, net_qty) VALUES ('{account}', 'TCS', 'INTRADAY', 10)");
        var (exitAll, _) = await store.AppendAsync(Draft("owner_button", HaltLevel.ExitAll, account), Ct);
        (await ReleaseMessageAsync(exitAll.Id, "flat soon")).ShouldContain("every position is flat");
        await ExecDatabase.ExecuteAsync(db.Owner, $"UPDATE execution.positions SET net_qty = 0 WHERE account_id = '{account}'");
        (await ReleaseMessageAsync(exitAll.Id, "flat now")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_a_code_that_clears_itself_gets_an_automatic_clear_by_the_execution_role_and_never_after_an_owner_release()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgHaltStore(data);
        var (feed, _) = await store.AppendAsync(Draft("feed_loss", HaltLevel.Entries, null, owner: false), Ct);
        var (breach, _) = await store.AppendAsync(Draft("protection_breach", HaltLevel.Entries, Guid.NewGuid(), owner: false), Ct);

        (await store.AutoClearAsync(breach.Id, "protection_breach", "x", At, Ct)).ShouldBeFalse();
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.halt_auto_clears (halt_id, at, trigger_code, detail) VALUES ({breach.Id}, now(), 'protection_breach', 'x')")).ShouldBe("23514", "the database refuses a code that does not clear itself");
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.halt_auto_clears (halt_id, at, trigger_code, detail) VALUES ({feed.Id}, now(), 'clock_skew', 'x')")).ShouldBe("23514", "it names the halt's own trigger");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.halt_auto_clears (halt_id, at, trigger_code, detail) VALUES ({feed.Id}, now(), 'feed_loss', 'x')")).ShouldBe("42501", "the API can not clear a halt this way");
        (await store.AutoClearAsync(feed.Id, "feed_loss", "60 s quiet", At, Ct)).ShouldBeTrue();
        (await store.AutoClearAsync(feed.Id, "feed_loss", "again", At, Ct)).ShouldBeFalse();
        (await AsAppReleaseAsync(feed.Id)).ShouldBe("23514", "a cleared halt can not also be released");

        var (skew, _) = await store.AppendAsync(Draft("clock_skew", HaltLevel.Entries, null, owner: false), Ct);
        (await AsAppReleaseAsync(skew.Id)).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.halt_auto_clears (halt_id, at, trigger_code, detail) VALUES ({skew.Id}, now(), 'clock_skew', 'x')")).ShouldBe("23514", "a released halt can not also be cleared");
    }

    [Fact]
    public async Task Day_marks_drills_and_the_kill_switch_log_are_insert_only_and_a_second_mark_for_a_day_changes_nothing()
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var account = db.AccountId;
        var marks = new PgDayMarkStore(data);
        var day = new DateOnly(2026, 10, 29);

        (await marks.AppendAsync(new DayMark(account, day, 100m, 120m, 500000m, 300m, At), Ct)).ShouldBeTrue();
        (await marks.AppendAsync(new DayMark(account, day, 999m, 999m, 500000m, 999m, At), Ct)).ShouldBeFalse();
        (await marks.AppendAsync(new DayMark(account, day.AddDays(1), 0m, -20000m, 500000m, 300m, At), Ct)).ShouldBeTrue();

        var latest = await marks.LatestAsync(account, 5, Ct);
        latest.Select(m => m.SessionDate).ShouldBe([day.AddDays(1), day]);
        latest[1].Marked.ShouldBe(120m);
        DayMarks.Drawdown(latest, day.AddDays(1), 8.0m).ShouldBe(TriggerVerdict.Clear, "20,300 is 4.06 percent of 500,000");
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.day_marks (account_id, session_date, realised, marked, capital, peak, at) VALUES ('{account}', '2026-11-02', 0, 50, 500000, 10, now())")).ShouldBe("23514", "a peak below the marked value is refused");
        (await ExecDatabase.SqlStateAsync(db.Owner, $"UPDATE execution.day_marks SET peak = 1 WHERE account_id = '{account}'")).ShouldBe("23514");

        await new PgDrillStore(data).AppendAsync(new DrillResult(account, true, 12, "seen=True", At), Ct);
        (await ExecDatabase.SqlStateAsync(db.Owner, "DELETE FROM execution.drills")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.App, "INSERT INTO execution.drills (at, account_id, passed, latency_ms, detail) VALUES (now(), gen_random_uuid(), true, 1, 'x')")).ShouldBe("42501");

        var (halt, _) = await new PgHaltStore(data).AppendAsync(Draft("owner_button", HaltLevel.Cancel, account), Ct);
        var log = new PgKillSwitchLog(data);
        await log.AppendAsync(halt.Id, "deferred", "x", At, Ct);
        await log.AppendAsync(halt.Id, "activated", "x", At, Ct);
        (await log.OutcomesAsync(halt.Id, Ct)).ShouldBe(["deferred", "activated"]);
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.kill_switch_log (at, halt_id, outcome, detail) VALUES (now(), {halt.Id}, 'maybe', 'x')")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Owner, "UPDATE execution.kill_switch_log SET detail = 'y'")).ShouldBe("23514");
    }
}
