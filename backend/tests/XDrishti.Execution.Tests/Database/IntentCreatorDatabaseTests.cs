using Microsoft.Extensions.Time.Testing;
using Npgsql;
using XDrishti.Execution.Application.Intents;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>
/// Spec P12-08 criterion 16 (P12-08a) on a real database, as the role xd_exec: an owner-approved ticket with an allocation becomes exactly one intent with its plan, ticket, approval and mode trail links; a second pass inserts nothing; the
/// intent is armed once the ticket is armed and moves to checking on a crossing while the stored readiness check is ready; the creator writes no <c>trading.*</c> row. The mode and the halt record are fakes: no account can be
/// <c>semi_auto</c> in the database yet (ADR 0163), which is the point of the closed mode source.
/// </summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class IntentCreatorDatabaseTests(ExecDatabase db)
{
    private const string Owner = "owner:hem@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Fakes(Guid account) : IModeSource, IHaltSource, IIntentSettings
    {
        public ModeAnswer? Answer { get; set; } = new(ExecMode.SemiAuto, true);

        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(accountId == account ? Answer : null);

        public Task<HaltReading> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(HaltReading.Clear);

        public IntentSettings? Current() => new(new TimeOnly(9, 20), new TimeOnly(15, 0), 2.0m, false, TimeSpan.FromSeconds(5));
    }

    private sealed record Seeded(Guid Account, long Plan, long Item, long Approval, long Trail, Guid Instrument, string Symbol, DateOnly Session);

    /// <summary>An account configured manual_live with one owner demotion in its trail, an instrument with a Dhan id, and a plan for <paramref name="session"/> whose one ticket the owner approved with an allocation of 120.</summary>
    private async Task<Seeded> SeedAsync(DateOnly session)
    {
        var account = Guid.NewGuid();
        var instrument = Guid.NewGuid();
        var tag = account.ToString("N")[..10];
        var symbol = "NSE:IC" + tag[..6].ToUpperInvariant();
        var client = Random.Shared.NextInt64(2_000_000_000, 9_000_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var day = session.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await ExecDatabase.ExecuteAsync(
            db.Owner,
            $"""
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES ('{account}', 'ic-{tag}', 'Intent {tag}', 'Dhan', '{client}', current_date, '#112233', now(), now(), false, false, false, 0);
            INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments)
            VALUES ('{account}', true, 'ManualLive', 'Fixed', 400000, true, false, 1, 0, 0);
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, series, lot_size, underlying_symbol, sector, fno_eligible, first_seen_at, last_seen_at)
            VALUES ('{instrument}', '{symbol}', 'NSE', 'NSE_EQ', 'share', 'Intent probe', '{symbol}', NULL, 'EQ', 1, '{symbol}', NULL, false, now(), now());
            INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES ('{instrument}', 'dhan', '{client[..7]}', 'NSE_EQ');
            INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at)
            VALUES ('intent-{tag}', '{day}', '{day} 03:35:00+00', 1, repeat('d', 64), 'b', true, 'n', 'l', 'probe', now());
            INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable,
                                            entry_ref_price, stop_level, stop_anchor, target_level, reward_risk, levels_session)
            SELECT id, 1, 'primary', '{symbol}', 'BUY', 'x', 'h', 't', 's', 't', 'v', ARRAY[]::text[], 'c', 1, 'd', 'b', ARRAY[]::text[], 412.35, 405.10, 'level', 425.80, 1.8276, '{day}' FROM trading.plans WHERE plan_key = 'intent-{tag}';
            INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, cutoff, decided_at)
            SELECT p.id, i.id, 'owner', 'approve', '{Owner}', 'ok', p.review_cutoff, now() FROM trading.plans p JOIN trading.plan_items i ON i.plan_id = p.id WHERE p.plan_key = 'intent-{tag}';
            INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, approval_id, prev_event_id, created_at)
            SELECT a.plan_id, a.item_id, 'proposed', 'approved', '{Owner}', 'ok', a.id, NULL, now() FROM workflow.approvals a JOIN trading.plans p ON p.id = a.plan_id WHERE p.plan_key = 'intent-{tag}';
            INSERT INTO trading.plan_item_allocations (item_id, account_id, mode, quantity)
            SELECT i.id, '{account}', 'paper', 120 FROM trading.plan_items i JOIN trading.plans p ON p.id = i.plan_id WHERE p.plan_key = 'intent-{tag}';
            """);
        await ExecDatabase.ExecuteAsync(
            db.App,
            $"INSERT INTO execution.mode_trail (account_id, from_mode, to_mode, kind, actor, reason, gate_snapshot, gate_hash, effective_from) VALUES ('{account}', 'manual_live', 'paper', 'owner_demotion', '{Owner}', 'r', '{{}}', repeat('b', 64), now())");
        var plan = (long)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT id FROM trading.plans WHERE plan_key = 'intent-{tag}'"))!;
        var item = (long)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT id FROM trading.plan_items WHERE plan_id = {plan}"))!;
        var approval = (long)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT id FROM workflow.approvals WHERE item_id = {item}"))!;
        var trail = (long)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT id FROM execution.mode_trail WHERE account_id = '{account}'"))!;
        return new(account, plan, item, approval, trail, instrument, symbol, session);
    }

    private static DateTimeOffset Ist(DateOnly session, int hour, int minute) => new(session.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromMinutes(330));

    private static IntentCreator Creator(NpgsqlDataSource data, Fakes fakes, FakeTimeProvider clock)
    {
        var plans = new PgIntentPlanSource(data);
        return new IntentCreator(plans, new PgIntentRecord(data), new PgExecEventStore(data), fakes, fakes, new PgReadinessStore(data), plans, fakes, ExecEnvironment.Sandbox, clock);
    }

    private async Task<(long Id, string State)> IntentAsync(Seeded s)
    {
        await using var connection = new NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand($"SELECT i.id, st.state FROM execution.intents i JOIN execution.intent_state st ON st.intent_id = i.id WHERE i.account_id = '{s.Account}'", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        var found = (reader.GetInt64(0), reader.GetString(1));
        (await reader.ReadAsync(Ct)).ShouldBeFalse();
        return found;
    }

    [Fact]
    public async Task An_approved_ticket_becomes_one_intent_with_its_links_armed_then_checking_and_no_trading_row_is_written()
    {
        var s = await SeedAsync(new DateOnly(2098, 3, 10));
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var fakes = new Fakes(s.Account);
        var clock = new FakeTimeProvider(Ist(s.Session.AddDays(-1), 20, 0));
        var tradingRows = await ExecDatabase.ScalarAsync(db.Owner, "SELECT (SELECT count(*) FROM trading.plan_events) + (SELECT count(*) FROM workflow.approvals) + (SELECT count(*) FROM trading.plan_item_allocations)");

        await Creator(data, fakes, clock).RunOnceAsync(Ct);
        await Creator(data, fakes, clock).RunOnceAsync(Ct);

        var (id, state) = await IntentAsync(s);
        state.ShouldBe("planned");
        var links = await ExecDatabase.ScalarAsync(db.Owner, $"SELECT concat_ws('|', plan_id, plan_item_id, approval_id, mode_trail_id, quantity, entry_price, stop_price, target_price, side, structure, environment, created_by, session_date) FROM execution.intents WHERE id = {id}");
        links.ShouldBe($"{s.Plan}|{s.Item}|{s.Approval}|{s.Trail}|120|412.3500|405.1000|425.8000|buy|super|sandbox|{IntentCreator.Actor}|{s.Session:yyyy-MM-dd}");

        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, prev_event_id, created_at) VALUES ({s.Plan}, {s.Item}, 'approved', 'armed', 'system:preopen', 'r', (SELECT max(id) FROM trading.plan_events WHERE item_id = {s.Item}), now())");
        tradingRows = (long)tradingRows! + 1;
        clock.SetUtcNow(Ist(s.Session, 9, 10));
        await Creator(data, fakes, clock).RunOnceAsync(Ct);
        (await IntentAsync(s)).State.ShouldBe("armed");

        var now = Ist(s.Session, 10, 0);
        clock.SetUtcNow(now);
        await ExecDatabase.ExecuteAsync(
            db.Owner,
            $"INSERT INTO execution.readiness_checks (at, account_id, session_date, verdict, reasons, token_expires_at, profile_read_at, ip_read_at) VALUES ('{now.AddMinutes(-1):O}', '{s.Account}', '{s.Session:yyyy-MM-dd}', 'ready', '[]', '{now.AddHours(8):O}', '{now.AddMinutes(-1):O}', '{now.AddMinutes(-1):O}')");
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO market.live_quotes (instrument_id, last_price, tick_at) VALUES ('{s.Instrument}', 412.40, '{now:O}')");
        await Creator(data, fakes, clock).RunOnceAsync(Ct);
        await Creator(data, fakes, clock).RunOnceAsync(Ct);

        (await IntentAsync(s)).State.ShouldBe("checking");
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT string_agg(to_state || ':' || (payload ->> 'reason') || ':' || actor, ',' ORDER BY seq) FROM execution.events WHERE intent_id = {id}"))
            .ShouldBe($"armed:armed:{IntentCreator.Actor},checking:triggered:{IntentCreator.Actor}");
        (await ExecDatabase.ScalarAsync(db.Owner, "SELECT (SELECT count(*) FROM trading.plan_events) + (SELECT count(*) FROM workflow.approvals) + (SELECT count(*) FROM trading.plan_item_allocations)")).ShouldBe(tradingRows);
    }

    [Fact]
    public async Task A_crossing_without_a_ready_check_cancels_the_intent_missed_not_ready_and_an_account_without_a_sending_mode_gets_none()
    {
        var s = await SeedAsync(new DateOnly(2098, 4, 14));
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var fakes = new Fakes(s.Account);
        var clock = new FakeTimeProvider(Ist(s.Session, 9, 10));
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, prev_event_id, created_at) VALUES ({s.Plan}, {s.Item}, 'approved', 'armed', 'system:preopen', 'r', (SELECT max(id) FROM trading.plan_events WHERE item_id = {s.Item}), now())");
        await Creator(data, fakes, clock).RunOnceAsync(Ct);
        await Creator(data, fakes, clock).RunOnceAsync(Ct);
        var now = Ist(s.Session, 11, 0);
        clock.SetUtcNow(now);
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO market.live_quotes (instrument_id, last_price, tick_at) VALUES ('{s.Instrument}', 415, '{now:O}')");

        await Creator(data, fakes, clock).RunOnceAsync(Ct);

        var (id, state) = await IntentAsync(s);
        state.ShouldBe("cancelled");
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT payload ->> 'reason' FROM execution.events WHERE intent_id = {id} ORDER BY seq DESC LIMIT 1")).ShouldBe(IntentReasons.MissedNotReady);

        var other = await SeedAsync(new DateOnly(2098, 4, 21));
        var assisted = new Fakes(other.Account) { Answer = new(ExecMode.Assisted, true) };
        clock.SetUtcNow(Ist(other.Session.AddDays(-1), 20, 0));
        await Creator(data, assisted, clock).RunOnceAsync(Ct);
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT count(*) FROM execution.intents WHERE account_id = '{other.Account}'")).ShouldBe(0L);
    }

    [Fact]
    public async Task The_execution_role_can_read_the_plan_side_but_cannot_write_a_trading_row()
    {
        var s = await SeedAsync(new DateOnly(2098, 5, 12));

        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO trading.plan_events (plan_id, item_id, from_state, to_state, actor, reason, prev_event_id, created_at) VALUES ({s.Plan}, {s.Item}, 'approved', 'armed', 'system:x', 'r', NULL, now())"))
            .ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, "SELECT count(*) FROM trading.plan_item_allocations, workflow.approvals, market.live_quotes, execution.mode_trail")).ShouldBeNull();
    }
}
