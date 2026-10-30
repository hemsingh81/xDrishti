using Npgsql;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Readiness;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// P12-11 criteria 1, 4, 9 and the grants on a real database, as the real roles: the three tables are insert-only (even for the owner), the execution service writes the checks and the day state and only reads the attestation, the API writes the attestation and only reads the rest, the
/// day state is unique per account and session, the database stamps the attestation time, and the store round-trips what the loop writes.
/// </summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecReadinessTests(ExecDatabase db)
{
    private const string Owner = "owner:hem@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Check(Guid account, string verdict = "not_ready", string session = "2026-10-27") =>
        $"INSERT INTO execution.readiness_checks (at, account_id, session_date, verdict, reasons) VALUES (now(), '{account}', '{session}', '{verdict}', '[]')";

    private static string Attest(Guid account, string actor = Owner, string at = "now()") =>
        $"INSERT INTO execution.ip_attestations (account_id, ip, orders_allowed, note, actor, at) VALUES ('{account}', '203.0.113.7', true, 'checked', '{actor}', {at})";

    [Fact]
    public async Task The_execution_role_writes_checks_and_day_states_the_app_role_writes_attestations_and_neither_writes_the_others()
    {
        var account = Guid.NewGuid();

        (await ExecDatabase.SqlStateAsync(db.Exec, Check(account))).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.day_states (account_id, session_date, state, reasons, late, at) VALUES ('{account}', '2026-10-27', 'unavailable', '[]', true, now())")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, Attest(account))).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, Attest(account))).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.App, Check(account))).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.day_states (account_id, session_date, state, reasons, late, at) VALUES ('{account}', '2026-10-28', 'ready', '[]', false, now())")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.App, "SELECT count(*) FROM execution.readiness_checks")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, "SELECT count(*) FROM execution.ip_attestations")).ShouldBeNull();
    }

    [Theory]
    [InlineData("readiness_checks")]
    [InlineData("day_states")]
    [InlineData("ip_attestations")]
    public async Task Every_table_is_insert_only_for_every_role_including_the_owner(string table)
    {
        var account = Guid.NewGuid();
        await ExecDatabase.ExecuteAsync(db.Owner, table switch
        {
            "readiness_checks" => Check(account),
            "day_states" => $"INSERT INTO execution.day_states (account_id, session_date, state, reasons, late, at) VALUES ('{account}', '2026-10-27', 'ready', '[]', false, now())",
            _ => Attest(account),
        });

        (await ExecDatabase.SqlStateAsync(db.Owner, $"UPDATE execution.{table} SET account_id = account_id")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Owner, $"DELETE FROM execution.{table}")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Owner, $"TRUNCATE execution.{table} CASCADE")).ShouldBe("23514");
    }

    [Fact]
    public async Task The_database_stamps_the_attestation_time_and_refuses_a_system_actor_and_a_blank_or_decorated_address()
    {
        var account = Guid.NewGuid();

        (await ExecDatabase.SqlStateAsync(db.App, Attest(account, at: "'2000-01-01'"))).ShouldBeNull();
        var age = (decimal)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT extract(epoch FROM now() - at) FROM execution.ip_attestations WHERE account_id = '{account}'"))!;

        age.ShouldBeInRange(-5m, 60m);
        (await ExecDatabase.SqlStateAsync(db.App, Attest(account, actor: "system:x"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.ip_attestations (account_id, ip, orders_allowed, note, actor) VALUES ('{account}', '203.0.113.7/32', true, '', '{Owner}')")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.ip_attestations (account_id, ip, orders_allowed, note, actor) VALUES ('{account}', '203.0.113.7', true, '{new string('x', 201)}', '{Owner}')")).ShouldBe("22001");
    }

    [Fact]
    public async Task A_ready_check_must_carry_its_evidence_and_a_day_state_is_unique_per_account_and_session()
    {
        var account = Guid.NewGuid();

        (await ExecDatabase.SqlStateAsync(db.Exec, Check(account, "ready"))).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, Check(account, "maybe"))).ShouldBe("23514");
        using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgReadinessStore(data);
        var draft = new DayStateDraft(account, new DateOnly(2026, 10, 27), ReadinessState.NotReady, [], null, false, DateTimeOffset.UtcNow);

        (await store.AppendDayStateAsync(draft, Ct)).ShouldBeTrue();
        (await store.AppendDayStateAsync(draft with { State = ReadinessState.Ready }, Ct)).ShouldBeFalse();
        (await store.AppendDayStateAsync(draft with { SessionDate = new DateOnly(2026, 10, 28) }, Ct)).ShouldBeTrue();
        (await store.HasDayStateAsync(account, new DateOnly(2026, 10, 27), Ct)).ShouldBeTrue();
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.day_states (account_id, session_date, state, reasons, late, at) VALUES ('{account}', '2026-10-29', 'ready', '[]', true, now())")).ShouldBe("23514"); // late is never ready
    }

    [Fact]
    public async Task The_store_round_trips_a_check_with_its_reasons_and_reads_the_newest_attestation_for_an_address()
    {
        var account = Guid.NewGuid();
        using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgReadinessStore(data);
        var at = new DateTimeOffset(2026, 10, 27, 3, 40, 30, TimeSpan.Zero);
        var reasons = new ReadinessReason[] { new("connected", ReadinessOutcome.Pass, "connected", "Dhan is connected."), new("ip_rejected", ReadinessOutcome.Fail, "ip_rejected", "x", null, at) };

        var stored = await store.AppendCheckAsync(new(at, account, new DateOnly(2026, 10, 27), ReadinessState.NotReady, reasons, at.AddHours(8), at, at, new DateOnly(2026, 10, 8)), Ct);
        var last = await store.LastCheckAsync(account, Ct);
        await ExecDatabase.ExecuteAsync(db.App, Attest(account));
        await ExecDatabase.ExecuteAsync(db.App, $"INSERT INTO execution.ip_attestations (account_id, ip, orders_allowed, note, actor) VALUES ('{account}', '203.0.113.7', false, 'later', '{Owner}')");

        last!.Id.ShouldBe(stored.Id);
        last.Verdict.ShouldBe(ReadinessState.NotReady);
        last.Reasons.Select(r => (r.Code, r.Outcome, r.Since)).ShouldBe([("connected", ReadinessOutcome.Pass, null), ("ip_rejected", ReadinessOutcome.Fail, at)]);
        (await store.NewestAttestationAsync(account, "203.0.113.7", Ct))!.OrdersAllowed.ShouldBeFalse();
        (await store.NewestAttestationAsync(account, "198.51.100.1", Ct)).ShouldBeNull();
        (await store.LastCheckAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_calendar_makes_weekdays_sessions_unless_a_confirmed_holiday_and_weekends_closed_unless_a_confirmed_special_session()
    {
        using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgReadinessStore(data);
        await ExecDatabase.ExecuteAsync(db.Owner, "INSERT INTO market.trading_days (date, kind, source, status, updated_at) VALUES ('2026-10-20', 'holiday', 'manual', 'confirmed', now()), ('2026-10-21', 'holiday', 'manual', 'proposed', now()) ON CONFLICT DO NOTHING");
        await ExecDatabase.ExecuteAsync(db.Owner, "INSERT INTO market.trading_days (date, kind, session_from, session_to, source, status, updated_at) VALUES ('2026-10-24', 'special', '18:00', '19:00', 'manual', 'confirmed', now()) ON CONFLICT DO NOTHING");

        (await store.IsTradingDayAsync(new DateOnly(2026, 10, 19), Ct)).ShouldBeTrue(); // Monday
        (await store.IsTradingDayAsync(new DateOnly(2026, 10, 20), Ct)).ShouldBeFalse(); // confirmed holiday
        (await store.IsTradingDayAsync(new DateOnly(2026, 10, 21), Ct)).ShouldBeTrue(); // a proposal is not a holiday
        (await store.IsTradingDayAsync(new DateOnly(2026, 10, 25), Ct)).ShouldBeFalse(); // Sunday
        (await store.IsTradingDayAsync(new DateOnly(2026, 10, 24), Ct)).ShouldBeTrue(); // Saturday with a special session
    }

    [Fact]
    public async Task The_connection_facts_read_only_status_and_expiry_and_a_missing_row_is_null_and_no_account_is_assisted_by_default()
    {
        using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgReadinessStore(data);

        (await store.ReadAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
        (await store.AssistedAccountsAsync(Ct)).ShouldNotContain(db.AccountId);
    }

    [Fact]
    public async Task The_highest_sends_per_second_query_counts_begin_rows_of_the_account_within_the_india_day()
    {
        var account = Guid.NewGuid();
        await ExecDatabase.ExecuteAsync(
            db.Owner,
            $"""
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES ('{account}', 's-{account:N}', 'Sends {account:N}', 'Dhan', '{Random.Shared.NextInt64(2_000_000_000, 9_000_000_000)}', current_date, '#112233', now(), now(), false, false, false, 0);
            INSERT INTO execution.broker_calls (at, account_id, call, phase, environment) VALUES
              ('2026-10-27T09:25:00.1+05:30', '{account}', 'orders_post', 'begin', 'sandbox'), ('2026-10-27T09:25:00.9+05:30', '{account}', 'orders_post', 'begin', 'sandbox'),
              ('2026-10-27T09:25:00.5+05:30', '{account}', 'orders_post', 'begin', 'sandbox'), ('2026-10-27T09:26:00+05:30', '{account}', 'orders_post', 'begin', 'sandbox'),
              ('2026-10-28T00:00:00+05:30', '{account}', 'orders_post', 'begin', 'sandbox')
            """);

        var peak = await ExecDatabase.ScalarAsync(
            db.Owner,
            $"""
            SELECT max(c) FROM (SELECT count(*) AS c FROM execution.broker_calls WHERE account_id = '{account}' AND call = 'orders_post' AND phase = 'begin'
              AND at >= '2026-10-27T00:00:00+05:30' AND at < '2026-10-28T00:00:00+05:30' GROUP BY date_trunc('second', at)) s
            """);

        peak.ShouldBe(3L);
    }
}
