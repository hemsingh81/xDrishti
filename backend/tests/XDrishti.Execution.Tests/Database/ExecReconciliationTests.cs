using Npgsql;
using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// P12-06 on a real database, as the real roles: runs, breaks and resolutions are insert-only, the resolution is the owner's row written by xd_app and checked by a trigger, xd_exec can not write it, a freeze caused by a reconciliation break is released only when
/// every break of the account has a resolution, and the read-only view of our books and the fill writer work against the real tables.
/// </summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecReconciliationTests(ExecDatabase db)
{
    private static readonly DateTimeOffset At = new(2026, 10, 30, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Session = new(2026, 10, 30);
    private static long _next = 1_500_000_000 + Random.Shared.NextInt64(0, 100_000_000);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> NewAccountAsync()
    {
        var account = Guid.NewGuid();
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES ('{account}', 'rec-{account:N}', 'R', 'Dhan', '{Random.Shared.NextInt64(2000000000, 2999999999)}', current_date, '#112233', now(), now(), false, false, false, 0)");
        return account;
    }

    private static ReconBreakDraft Draft(BreakKind kind = BreakKind.PositionMismatch, string key = "position_mismatch:1594:13", long? intent = null) => new(kind, key, "TCS", intent, "10", "13", "the broker's net differs");

    private async Task<(long Run, long Break)> OneBreakAsync(Guid account, BreakKind kind = BreakKind.PositionMismatch, string key = "position_mismatch:1594:13")
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgReconStore(data);
        var run = await store.RecordRunAsync(account, ReconKind.Continuous, true, 1, 1, 1, 1, [Draft(kind, key)], At, Ct);
        var id = (await store.BreaksAsync(account, Ct)).Single(b => b.Key == key).Id;
        return (run, id);
    }

    private Task<string?> ResolveAsync(long breakId, string resolution = "accept_broker", string by = "owner:hem@example.test", string? connection = null) =>
        ExecDatabase.SqlStateAsync(connection ?? db.App, $"INSERT INTO execution.break_resolutions (break_id, resolved_by, resolution) VALUES ({breakId}, '{by}', '{resolution}')");

    [Fact]
    public async Task A_run_and_its_breaks_round_trip_with_the_open_and_resolved_keys_and_the_runs_of_the_session()
    {
        var account = await NewAccountAsync();
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgReconStore(data);

        await store.RecordRunAsync(account, ReconKind.Eod, true, 3, 2, 1, 2, [Draft(), Draft(BreakKind.TradeExtra, "trade_extra:T1")], At, Ct);
        await store.RecordRunAsync(account, ReconKind.Final, false, 0, 0, 0, 1, [Draft(BreakKind.Unavailable, "unavailable:order_book_unreadable")], At, Ct);

        (await store.OpenKeysAsync(account, Ct)).Order().ShouldBe(["position_mismatch:1594:13", "trade_extra:T1", "unavailable:order_book_unreadable"]);
        var breaks = await store.BreaksAsync(account, Ct);
        breaks.Count.ShouldBe(3);
        breaks.ShouldAllBe(b => b.Resolution == null);
        (await ResolveAsync(breaks.First(b => b.Kind == BreakKind.TradeExtra).Id)).ShouldBeNull();
        (await store.ResolvedKeysAsync(account, Session, Ct)).ShouldBe(["trade_extra:T1"]);
        (await store.ResolvedKeysAsync(account, Session.AddDays(1), Ct)).ShouldBeEmpty("a resolution explains the day of its break");
        (await store.OpenKeysAsync(account, Ct)).ShouldNotContain("trade_extra:T1");
        var runs = await store.RunsAsync(account, Session, Ct);
        runs.Select(r => (r.Kind, r.Readable, r.Breaks)).ShouldBe([(ReconKind.Eod, true, 2), (ReconKind.Final, false, 1)]);
        (await store.BreaksAsync(account, Ct)).Single(b => b.Kind == BreakKind.TradeExtra).Resolution!.By.ShouldBe("owner:hem@example.test");
    }

    [Fact]
    public async Task The_three_tables_refuse_edits_deletes_and_truncates_even_to_the_owner_and_an_unreadable_run_must_carry_a_break()
    {
        var account = await NewAccountAsync();
        var (run, brk) = await OneBreakAsync(account);
        (await ResolveAsync(brk)).ShouldBeNull();

        foreach (var statement in new[]
        {
            $"UPDATE execution.recon_runs SET breaks = 0 WHERE id = {run}", $"DELETE FROM execution.recon_runs WHERE id = {run}", "TRUNCATE execution.recon_runs CASCADE",
            $"UPDATE execution.recon_breaks SET kind = 'unavailable' WHERE id = {brk}", $"DELETE FROM execution.recon_breaks WHERE id = {brk}", "TRUNCATE execution.recon_breaks CASCADE",
            $"UPDATE execution.break_resolutions SET note = 'x' WHERE break_id = {brk}", $"DELETE FROM execution.break_resolutions WHERE break_id = {brk}", "TRUNCATE execution.break_resolutions CASCADE",
        })
        {
            (await ExecDatabase.SqlStateAsync(db.Owner, statement)).ShouldBe("23514", statement);
        }

        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.recon_runs (at, account_id, session_date, kind, readable, orders, trades, positions, breaks) VALUES (now(), '{account}', current_date, 'eod', false, 0, 0, 0, 0)")).ShouldBe("23514", "unreadable and clean at once is refused");
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.recon_runs (at, account_id, session_date, kind, readable, orders, trades, positions, breaks) VALUES (now(), '{account}', current_date, 'weekly', true, 0, 0, 0, 0)")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.recon_breaks (run_id, account_id, session_date, kind, severity, key, ours, theirs, detail, at) VALUES ({run}, '{account}', current_date, 'made_up', 'warn', 'k', '1', '2', 'd', now())")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.recon_runs (at, account_id, session_date, kind, readable, orders, trades, positions, breaks) VALUES (now(), '{account}', current_date, 'eod', true, 0, 0, 0, 0)")).ShouldBe("42501", "the API can not write a run");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.recon_breaks (run_id, account_id, session_date, kind, severity, key, ours, theirs, detail, at) VALUES ({run}, '{account}', current_date, 'unavailable', 'warn', 'k', '1', '2', 'd', now())")).ShouldBe("42501");
    }

    [Fact]
    public async Task The_resolution_is_written_by_the_app_role_only_by_an_owner_once_and_dismiss_foreign_only_for_a_foreign_conflict()
    {
        var account = await NewAccountAsync();
        var (_, plain) = await OneBreakAsync(account);
        var (_, foreign) = await OneBreakAsync(account, BreakKind.ForeignConflict, "foreign_conflict:M1");

        (await ResolveAsync(plain, connection: db.Exec)).ShouldBe("42501", "xd_exec has no INSERT on the owner act table");
        (await ResolveAsync(plain, by: "system:reconciliation")).ShouldBe("23514");
        (await ResolveAsync(plain, by: "owner:")).ShouldBe("23514");
        (await ResolveAsync(plain, resolution: "make_it_so")).ShouldBe("23514");
        (await ResolveAsync(plain, resolution: "dismiss_foreign")).ShouldBe("23514", "an order or position break is not a foreign item");
        (await ResolveAsync(9_999_999)).ShouldBe("23514", "no such break");
        (await ResolveAsync(foreign, resolution: "dismiss_foreign")).ShouldBeNull();
        (await ResolveAsync(plain)).ShouldBeNull();
        (await ResolveAsync(plain)).ShouldBe("23514", "a break is resolved once");
        (await ExecDatabase.SqlStateAsync(db.Exec, "SELECT count(*) FROM execution.break_resolutions")).ShouldBeNull("xd_exec can read it");
    }

    private async Task<string> ReleaseMessageAsync(long haltId)
    {
        try
        {
            await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.halt_releases (halt_id, released_by, reason) VALUES ({haltId}, 'owner:hem@example.test', 'reviewed')");
            return string.Empty;
        }
        catch (PostgresException failure)
        {
            return failure.MessageText;
        }
    }

    private static HaltDraft Freeze(Guid? account, string code = "reconciliation_break") =>
        new(account is null ? HaltScope.System : HaltScope.Account, account, XDrishti.Execution.Application.Ports.HaltLevel.Freeze, code, "system", "system:halts", "break", At);

    [Fact]
    public async Task A_freeze_from_a_reconciliation_break_is_released_only_when_the_account_has_a_break_and_every_break_has_a_resolution()
    {
        var account = await NewAccountAsync();
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var halts = new PgHaltStore(data);
        var (freeze, _) = await halts.AppendAsync(Freeze(account), Ct);

        (await ReleaseMessageAsync(freeze.Id)).ShouldContain("none on record");
        var (_, one) = await OneBreakAsync(account);
        var (_, two) = await OneBreakAsync(account, BreakKind.TradeExtra, "trade_extra:T9");
        (await ReleaseMessageAsync(freeze.Id)).ShouldContain("every break of the account has a resolution");
        (await ResolveAsync(one)).ShouldBeNull();
        (await ReleaseMessageAsync(freeze.Id)).ShouldContain("every break of the account has a resolution");
        (await ResolveAsync(two)).ShouldBeNull();
        (await ReleaseMessageAsync(freeze.Id)).ShouldBeEmpty();
        (await halts.ActiveAsync(Ct)).ShouldNotContain(h => h.Id == freeze.Id);
    }

    [Fact]
    public async Task Another_accounts_unresolved_break_does_not_hold_an_account_freeze_but_holds_a_system_freeze_and_a_freeze_of_another_cause_stays()
    {
        var mine = await NewAccountAsync();
        var theirs = await NewAccountAsync();
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var halts = new PgHaltStore(data);
        var (_, resolved) = await OneBreakAsync(mine);
        await OneBreakAsync(theirs);
        (await ResolveAsync(resolved)).ShouldBeNull();
        var (accountFreeze, _) = await halts.AppendAsync(Freeze(mine), Ct);
        var (oversold, _) = await halts.AppendAsync(Freeze(mine, "oversold"), Ct);
        var (systemFreeze, _) = await halts.AppendAsync(Freeze(null), Ct);

        (await ReleaseMessageAsync(oversold.Id)).ShouldContain("only a freeze caused by a reconciliation break");
        (await ReleaseMessageAsync(systemFreeze.Id)).ShouldContain("every break of the account has a resolution");
        (await ReleaseMessageAsync(accountFreeze.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_new_alarm_codes_and_the_foreign_conflict_halt_trigger_are_accepted_and_an_unknown_code_is_still_refused()
    {
        var account = await NewAccountAsync();
        await using var data = NpgsqlDataSource.Create(db.Exec);
        foreach (var code in new[] { "recovery_slow", "journal_gap", "foreign_conflict" })
        {
            (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.alarms (at, code, severity, account_id, detail) VALUES (now(), '{code}', 'warn', '{account}', 'd')")).ShouldBeNull(code);
        }

        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.alarms (at, code, severity, account_id, detail) VALUES (now(), 'made_up', 'warn', '{account}', 'd')")).ShouldBe("23514");
        var (halt, created) = await new PgHaltStore(data).AppendAsync(new HaltDraft(HaltScope.Account, account, XDrishti.Execution.Application.Ports.HaltLevel.Entries, "foreign_conflict", "system", "system:halts", "a manual order", At), Ct);
        created.ShouldBeTrue();
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.halt_releases (halt_id, released_by, reason) VALUES ({halt.Id}, 'owner:hem', 'seen')")).ShouldBeNull("the foreign conflict ends by the owner's release");
    }

    [Fact]
    public async Task Our_books_are_read_from_the_real_tables_and_the_fill_writer_is_idempotent_by_exchange_trade_id()
    {
        var account = await NewAccountAsync();
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var id = Interlocked.Increment(ref _next);
        var intent = ExecIntent.Create(id, Guid.NewGuid(), account, id, ExecSide.Buy, "TCS", "11536", 10, 100m, 98m, 104m, ExecStructure.PlainStop).Value;
        (await new PgExecEventStore(data).StartAsync(intent, Session, ExecEnvironment.Sandbox, "system:test", Ct)).IsSuccess.ShouldBeTrue();
        var cid = new ClientOrderId(Session, id, LegKind.Entry, 1).ToString();
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.legs (intent_id, kind, client_order_id, broker_order_id, status, qty, filled_qty, price) VALUES ({id}, 'E', '{cid}', 'B1', 'part_traded', 10, 4, 100)");
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.positions (account_id, symbol, product, net_qty) VALUES ('{account}', 'TCS', 'INTRADAY', 4)");
        var writer = new PgFillWriter(data);

        (await writer.RecordAsync(new FillRow(id, cid, "T-77", 4, 100.1234m, At), Ct)).ShouldBeTrue();
        (await writer.RecordAsync(new FillRow(id, cid, "T-77", 4, 100.1234m, At), Ct)).ShouldBeFalse();
        (await writer.RecordAsync(new FillRow(id, "XDNOSUCHLEG00001", "T-78", 1, 100m, At), Ct)).ShouldBeFalse("a trade for no leg of ours writes nothing");

        var view = await new PgReconOurSource(data).LoadAsync(account, Ct);
        var leg = view.Legs.ShouldHaveSingleItem();
        (leg.ClientOrderId, leg.BrokerOrderId, leg.Status, leg.Kind, leg.FilledQty, leg.SecurityId, leg.Side).ShouldBe((cid, "B1", LegStatus.PartTraded, LegKind.Entry, 4, "11536", ExecSide.Buy));
        view.Fills.ShouldHaveSingleItem().ShouldBe(new OurFillView("T-77", cid, 4, 100.1234m, "11536", ExecSide.Buy));
        view.Positions.ShouldBe([new OurPositionView("11536", 4)]);
        view.Intents.ShouldHaveSingleItem().State.ShouldBe(ExecState.Planned);
        (await new PgReconOurSource(data).AccountsAsync(Ct)).ShouldContain(account);
    }
}
