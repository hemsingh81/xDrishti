using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Execution;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;
using XDrishti.Execution.Persistence;
using XDrishti.Infrastructure.Execution;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>Its own server: these tests need an account whose effective mode is <c>semi_auto</c> and no active system halt, which the shared execution fixture cannot promise.</summary>
[CollectionDefinition(Name)]
public sealed class ConfirmDatabaseSuite : ICollectionFixture<ExecDatabase>
{
    public const string Name = "confirm-database";
}

/// <summary>
/// Spec P12-08 criteria 1, 4, 6, 8 and 11 on a real database with the real roles: the issuer stores a ticket as xd_exec whose hash the database checks; the trigger admits a confirmed outcome only from the owner through the API, before the
/// expiry by the database clock, with a day unlock, no halt, the switch ON and the account's effective mode <c>semi_auto</c>; twenty parallel taps leave one row; a tap racing the expiry leaves one row; the tables are insert-only.
/// No account can be <c>semi_auto</c> (ADR 0163), so <see cref="SemiAutoAsync"/> writes that one trail row as the owner past the constraint and the trigger, as earlier P12 database tests set up rows the record cannot reach yet; the
/// constraint is put back (NOT VALID) and still refuses every new <c>semi_auto</c> row.
/// </summary>
[Collection(ConfirmDatabaseSuite.Name)]
public sealed class ExecutionTicketsDatabaseTests(ExecDatabase db)
{
    private const string Owner = "owner:hem@example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Seeded(Guid Account, long IntentId, Guid IntentPublic, DateOnly Session, Guid Instrument);

    private sealed class Fakes : IModeSource, IIntentQuotes, IIntentSettings, IConfirmSettings
    {
        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<ModeAnswer?>(new ModeAnswer(ExecMode.SemiAuto, true));

        public LivePrice? Price { get; set; }

        public Task<LivePrice?> LastAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(Price);

        IntentSettings? IIntentSettings.Current() => new(new TimeOnly(0, 0), new TimeOnly(23, 59), 2.0m, false, TimeSpan.FromSeconds(5));

        ConfirmSettings? IConfirmSettings.Current() => ConfirmSettings.Default;
    }

    private sealed class NoCommands : IExecutionCommands
    {
        public Task<Guid> EnqueueAsync(string kind, Guid? accountId, string payloadJson, string actor, CancellationToken cancellationToken) => throw new InvalidOperationException("no refusal expected");
    }

    private sealed class NoPassword : ICommandHandler<StepUpCommand, StepUpResult>
    {
        public ValueTask<Result<StepUpResult>> HandleAsync(StepUpCommand command, CancellationToken cancellationToken) => throw new InvalidOperationException("no password expected");
    }

    private async Task<DateTimeOffset> DbNowAsync()
    {
        await using var connection = new NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        await reader.ReadAsync(Ct);
        return reader.GetFieldValue<DateTimeOffset>(0);
    }

    private async Task<DateOnly> TodayAsync() =>
        DateOnly.Parse((string)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT ((clock_timestamp() AT TIME ZONE 'Asia/Kolkata')::date)::text"))!, CultureInfo.InvariantCulture);

    /// <summary>An account configured manual_live with fixed capital, an instrument, and one BUY intent of today in checking (moved through the event chain as xd_exec) with a full run that passed every Hard check.</summary>
    private async Task<Seeded> SeedAsync(params string[] softFails)
    {
        var account = Guid.NewGuid();
        var instrument = Guid.NewGuid();
        var tag = account.ToString("N")[..10];
        var client = Random.Shared.NextInt64(2_000_000_000, 9_000_000_000).ToString(CultureInfo.InvariantCulture);
        var session = await TodayAsync();
        await ExecDatabase.ExecuteAsync(
            db.Owner,
            $"""
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES ('{account}', 'cf-{tag}', 'Confirm {tag}', 'Dhan', '{client}', current_date, '#112233', now(), now(), false, false, false, 0);
            INSERT INTO accounts.trading_settings (account_id, enabled, mode, capital_source, capital_amount, intraday, swing, intraday_instruments, swing_buy_instruments, swing_sell_instruments)
            VALUES ('{account}', true, 'ManualLive', 'Fixed', 600000, true, false, 1, 0, 0);
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, series, lot_size, underlying_symbol, sector, fno_eligible, first_seen_at, last_seen_at)
            VALUES ('{instrument}', 'NSE:CF{tag[..6].ToUpperInvariant()}', 'NSE', 'NSE_EQ', 'share', 'Confirm probe', 'probe', NULL, 'EQ', 1, 'probe', NULL, false, now(), now());
            INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at)
            VALUES ('confirm-{tag}', '{session:yyyy-MM-dd}', now() + interval '1 hour', {Random.Shared.Next(2, int.MaxValue)}, repeat('d', 64), 'b', true, 'n', 'l', 'probe', now());
            INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable)
            SELECT id, 1, 'primary', 'NSE:CF{tag[..6].ToUpperInvariant()}', 'BUY', 'x', 'h', 't', 's', 't', 'v', ARRAY[]::text[], 'c', 1, 'd', 'b', ARRAY[]::text[] FROM trading.plans WHERE plan_key = 'confirm-{tag}';
            INSERT INTO workflow.approvals (plan_id, item_id, kind, decision, actor, reason, cutoff, decided_at)
            SELECT p.id, i.id, 'owner', 'approve', '{Owner}', 'ok', p.review_cutoff, now() FROM trading.plans p JOIN trading.plan_items i ON i.plan_id = p.id WHERE p.plan_key = 'confirm-{tag}';
            """);
        var (item, approval) = ((long)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT i.id FROM trading.plan_items i JOIN trading.plans p ON p.id = i.plan_id WHERE p.plan_key = 'confirm-{tag}'"))!,
            (long)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT a.id FROM workflow.approvals a JOIN trading.plans p ON p.id = a.plan_id WHERE p.plan_key = 'confirm-{tag}'"))!);
        var intent = (long)(await ExecDatabase.ScalarAsync(
            db.Exec,
            $"INSERT INTO execution.intents (account_id, plan_item_id, approval_id, session_date, symbol, security_id, side, quantity, entry_price, stop_price, target_price, structure, environment, created_by) VALUES ('{account}', {item}, {approval}, '{session:yyyy-MM-dd}', 'NSE:CF{tag[..6].ToUpperInvariant()}', '3426', 'buy', 1, 412.35, 405.10, 425.80, 'super', 'sandbox', 'system:test') RETURNING id"))!;
        await ExecDatabase.ExecuteAsync(
            db.Exec,
            $"""
            INSERT INTO execution.events (intent_id, seq, kind, from_state, to_state, prev_event_id, source, actor) VALUES ({intent}, 1, 'state', 'planned', 'armed', NULL, 'engine', 'system:test');
            INSERT INTO execution.events (intent_id, seq, kind, from_state, to_state, prev_event_id, source, actor) SELECT {intent}, 2, 'state', 'armed', 'checking', e.id, 'engine', 'system:test' FROM execution.events e WHERE e.intent_id = {intent} AND e.seq = 1;
            """);
        var run = (long)(await ExecDatabase.ScalarAsync(
            db.Exec,
            $"INSERT INTO execution.check_runs (kind, intent_id, at, verdict, settings_hash, inputs_hash, valid_until) VALUES ('full', {intent}, now(), '{(softFails.Length > 0 ? "rejected" : "cleared")}', repeat('a', 16), repeat('b', 16), now() + interval '10 seconds') RETURNING id"))!;
        var rows = new List<string> { $"({run}, 0, 'gate_open', 'hard', 'pass', NULL, NULL, 'ok', NULL, 'test')" };
        rows.AddRange(softFails.Select((c, i) => $"({run}, {i + 1}, '{c}', 'soft', 'fail', NULL, NULL, 'x', NULL, 'test')"));
        await ExecDatabase.ExecuteAsync(db.Exec, "INSERT INTO execution.check_results (run_id, ord, code, class, outcome, value, limit_value, detail, input_age_ms, source) VALUES " + string.Join(", ", rows));
        var publicId = (Guid)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT public_id FROM execution.intents WHERE id = {intent}"))!;
        return new Seeded(account, intent, publicId, session, instrument);
    }

    /// <summary>The issuer of P12-08b over the real store, as xd_exec, with the clock set to the database's.</summary>
    private async Task<StoredTicket> IssueAsync(Seeded s)
    {
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var now = await DbNowAsync();
        var fakes = new Fakes { Price = new LivePrice(412.30m, now) };
        var result = await new ConfirmTicketIssuer(new PgConfirmTickets(data), fakes, fakes, fakes, fakes, ExecEnvironment.Sandbox, new FakeTimeProvider(now)).IssueAsync(s.IntentId, Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        return result.Value;
    }

    /// <summary>A ticket issued <paramref name="issuedAgoSeconds"/> ago (so that it expires 90 seconds after that), written by the owner past the issue-time trigger: the only way to reach an expiry within a test.</summary>
    private async Task<Guid> OldTicketAsync(Seeded s, double issuedAgoSeconds)
    {
        var id = Guid.NewGuid();
        await ExecDatabase.ExecuteAsync(db.Owner, "ALTER TABLE execution.confirm_tickets DISABLE TRIGGER trg_exec_confirm_tickets_check");
        try
        {
            var ago = issuedAgoSeconds.ToString(CultureInfo.InvariantCulture);
            await ExecDatabase.ExecuteAsync(
                db.Owner,
                $"""
                INSERT INTO execution.confirm_tickets (id, intent_public_id, account_id, plan_item_id, session_date, attempt, kind, params, text, text_hash, client_order_id, ref_price, notional, risk_at_stop, requires_password, password_reasons, issued_at, expires_at)
                SELECT '{id}', i.public_id, i.account_id, i.plan_item_id, i.session_date, 1, 'entry', jsonb_build_object(), 'probe ticket', encode(sha256(convert_to('probe ticket', 'UTF8')), 'hex'), 'XD261027000001E1', 412.30, 412.35, 7.25, false, '[]',
                       clock_timestamp() - interval '{ago} seconds', clock_timestamp() - interval '{ago} seconds' + interval '90 seconds'
                  FROM execution.intents i WHERE i.id = {s.IntentId}
                """);
        }
        finally
        {
            await ExecDatabase.ExecuteAsync(db.Owner, "ALTER TABLE execution.confirm_tickets ENABLE TRIGGER trg_exec_confirm_tickets_check");
        }

        return id;
    }

    /// <summary>The one row no slice can write yet: an effective mode of semi_auto. Written by the owner past the mode check and trigger, which are put back at once.</summary>
    private async Task SemiAutoAsync(Guid account)
    {
        await using var connection = new NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        await using var command = new NpgsqlCommand(
            $"""
            ALTER TABLE execution.mode_trail DROP CONSTRAINT IF EXISTS ck_exec_mode_trail_modes;
            ALTER TABLE execution.mode_trail DISABLE TRIGGER trg_exec_mode_trail_check;
            INSERT INTO execution.mode_trail (account_id, from_mode, to_mode, kind, actor, reason, gate_snapshot, gate_hash, effective_from)
            VALUES ('{account}', 'manual_live', 'semi_auto', 'owner_demotion', '{Owner}', 'test only: no slice can store semi_auto (ADR 0163)', jsonb_build_object(), repeat('b', 64), now() - interval '1 minute');
            ALTER TABLE execution.mode_trail ENABLE TRIGGER trg_exec_mode_trail_check;
            ALTER TABLE execution.mode_trail ADD CONSTRAINT ck_exec_mode_trail_modes CHECK (from_mode IN ('paper','manual_live','assisted') AND to_mode IN ('paper','manual_live','assisted') AND from_mode <> to_mode) NOT VALID;
            """, connection, tx);
        await command.ExecuteNonQueryAsync(Ct);
        await tx.CommitAsync(Ct);
    }

    private Task SwitchAsync(bool on) => ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.settings (execution_enabled, actor, reason) VALUES ({(on ? "true" : "false")}, '{Owner}', 'test')");

    /// <summary>An unlock valid for the next hour, written by the owner past the trigger (which would refuse it after 15:30 India time, when tests may run).</summary>
    private async Task UnlockAsync(Seeded s)
    {
        await ExecDatabase.ExecuteAsync(db.Owner, "ALTER TABLE execution.day_unlocks DISABLE TRIGGER trg_exec_day_unlocks_check");
        try
        {
            await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO execution.day_unlocks (account_id, session_date, unlocked_at, expires_at, actor) VALUES ('{s.Account}', '{s.Session:yyyy-MM-dd}', now(), now() + interval '1 hour', '{Owner}')");
        }
        finally
        {
            await ExecDatabase.ExecuteAsync(db.Owner, "ALTER TABLE execution.day_unlocks ENABLE TRIGGER trg_exec_day_unlocks_check");
        }
    }

    private Task ReadyAsync(Seeded s) => ExecDatabase.ExecuteAsync(
        db.Owner,
        $"INSERT INTO execution.readiness_checks (at, account_id, session_date, verdict, reasons, token_expires_at, profile_read_at, ip_read_at) VALUES (clock_timestamp(), '{s.Account}', '{s.Session:yyyy-MM-dd}', 'ready', '[]', now() + interval '8 hours', now(), now())");

    private Task QuoteAsync(Seeded s, decimal last) => ExecDatabase.ExecuteAsync(
        db.Owner,
        $"INSERT INTO market.live_quotes (instrument_id, last_price, tick_at) VALUES ('{s.Instrument}', {last.ToString(CultureInfo.InvariantCulture)}, clock_timestamp()) ON CONFLICT (instrument_id) DO UPDATE SET last_price = EXCLUDED.last_price, tick_at = EXCLUDED.tick_at");

    private static string Outcome(Guid ticket, string outcome, string actor, string? note = null) =>
        $"INSERT INTO execution.ticket_outcomes (ticket_id, outcome, reason, actor, note) VALUES ('{ticket}', '{outcome}', 'r', '{actor}', {(note is null ? "NULL" : $"'{note}'")})";

    private static async Task<string> RefusalAsync(string connection, string sql)
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

    private XDrishtiDbContext AppContext() => new(new DbContextOptionsBuilder<XDrishtiDbContext>().UseNpgsql(db.App).UseSnakeCaseNamingConvention().Options);

    [Fact]
    public async Task The_issuer_stores_one_ticket_as_xd_exec_whose_hash_the_database_checks_a_second_hand_over_returns_it_and_the_record_is_insert_only()
    {
        var s = await SeedAsync();

        var ticket = await IssueAsync(s);
        var again = await IssueAsync(s);

        again.Id.ShouldBe(ticket.Id);
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT count(*) FROM execution.confirm_tickets WHERE intent_public_id = '{s.IntentPublic}'")).ShouldBe(1L);
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT concat_ws('|', attempt, kind, ref_price, notional, risk_at_stop, requires_password, client_order_id, expires_at - issued_at, text_hash = encode(sha256(convert_to(text, 'UTF8')), 'hex')) FROM execution.confirm_tickets WHERE id = '{ticket.Id}'"))
            .ShouldBe($"1|entry|412.3000|412.35|7.25|f|{new ClientOrderId(s.Session, s.IntentId, LegKind.Entry, 1)}|00:01:30|t");
        foreach (var statement in new[] { $"UPDATE execution.confirm_tickets SET text = 'x' WHERE id = '{ticket.Id}'", $"DELETE FROM execution.confirm_tickets WHERE id = '{ticket.Id}'", "TRUNCATE execution.confirm_tickets CASCADE", "TRUNCATE execution.ticket_outcomes", "TRUNCATE execution.day_unlocks" })
        {
            (await ExecDatabase.SqlStateAsync(db.Owner, statement)).ShouldBe("23514", statement);
        }

        (await RefusalAsync(db.App, $"INSERT INTO execution.confirm_tickets (intent_public_id, account_id, plan_item_id, session_date, attempt, kind, params, text, text_hash, client_order_id, ref_price, notional, risk_at_stop, requires_password, password_reasons, issued_at, expires_at) SELECT intent_public_id, account_id, plan_item_id, session_date, 2, kind, params, text, text_hash, client_order_id, ref_price, notional, risk_at_stop, requires_password, password_reasons, issued_at, expires_at FROM execution.confirm_tickets WHERE id = '{ticket.Id}'"))
            .ShouldStartWith("42501");
    }

    private static string RawTicket(long intentId, int attempt, string issued, string text = "probe ticket", string? hashOf = null) =>
        $"""
        INSERT INTO execution.confirm_tickets (intent_public_id, account_id, plan_item_id, session_date, attempt, kind, params, text, text_hash, client_order_id, ref_price, notional, risk_at_stop, requires_password, password_reasons, issued_at, expires_at)
        SELECT i.public_id, i.account_id, i.plan_item_id, i.session_date, {attempt}, 'entry', jsonb_build_object(), '{text}', encode(sha256(convert_to('{hashOf ?? text}', 'UTF8')), 'hex'), 'XD261027000001E1', 412.30, 412.35, 7.25, false, '[]', {issued}, {issued} + interval '90 seconds'
          FROM execution.intents i WHERE i.id = {intentId}
        """;

    [Fact]
    public async Task A_second_ticket_follows_only_a_refused_first_and_a_ticket_is_issued_only_now_only_for_a_checking_intent_and_only_with_the_hash_of_its_text()
    {
        var s = await SeedAsync();
        var first = await IssueAsync(s);

        (await RefusalAsync(db.Exec, RawTicket(s.IntentId, 2, "clock_timestamp()"))).ShouldContain("only a refused first");
        await using (var data = NpgsqlDataSource.Create(db.Exec))
        {
            (await new TicketCloser(new PgConfirmTickets(data)).RefuseAsync(first.Id, ConfirmCodes.PriceMoved, Ct)).ShouldBe(true);
        }

        (await RefusalAsync(db.Exec, RawTicket(s.IntentId, 2, "clock_timestamp() - interval '10 seconds'"))).ShouldContain("issued now");
        (await RefusalAsync(db.Exec, RawTicket(s.IntentId, 2, "clock_timestamp()", hashOf: "another text"))).ShouldContain("ck_exec_confirm_tickets_text");
        (await RefusalAsync(db.Exec, RawTicket(s.IntentId, 3, "clock_timestamp()"))).ShouldContain("ck_exec_confirm_tickets_attempt");
        (await RefusalAsync(db.Exec, RawTicket(s.IntentId, 2, "clock_timestamp()"))).ShouldBeEmpty();

        var cancelled = await SeedAsync();
        await ExecDatabase.ExecuteAsync(db.Exec, $"INSERT INTO execution.events (intent_id, seq, kind, from_state, to_state, prev_event_id, source, actor) SELECT {cancelled.IntentId}, 3, 'state', 'checking', 'cancelled', e.id, 'engine', 'system:test' FROM execution.events e WHERE e.intent_id = {cancelled.IntentId} AND e.seq = 2");
        (await RefusalAsync(db.Exec, RawTicket(cancelled.IntentId, 1, "clock_timestamp()"))).ShouldContain("only for an intent in checking");
    }

    [Fact]
    public async Task The_trigger_admits_a_confirmed_outcome_only_from_the_owner_through_the_api_with_the_switch_on_semi_auto_an_unlock_and_no_halt()
    {
        var s = await SeedAsync("adv_share");
        var ticket = await IssueAsync(s);
        var halted = await SeedAsync();
        var haltedTicket = await IssueAsync(halted);

        (await RefusalAsync(db.Exec, Outcome(ticket.Id, "confirmed", Owner, "known"))).ShouldContain("owner's act");
        (await RefusalAsync(db.App, Outcome(ticket.Id, "confirmed", "system:x", "known"))).ShouldContain("owner's act");
        await SwitchAsync(false);
        (await RefusalAsync(db.App, Outcome(ticket.Id, "confirmed", Owner, "known"))).ShouldContain("execution.disabled");
        await SwitchAsync(true);
        (await RefusalAsync(db.App, Outcome(ticket.Id, "confirmed", Owner, "known"))).ShouldContain("execution.mode_changed");
        await SemiAutoAsync(s.Account);
        await SemiAutoAsync(halted.Account);
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT execution.effective_mode('{s.Account}')")).ShouldBe("semi_auto");
        (await RefusalAsync(db.App, Outcome(ticket.Id, "confirmed", Owner, "known"))).ShouldContain("execution.not_unlocked");
        await UnlockAsync(s);
        await UnlockAsync(halted);
        (await RefusalAsync(db.App, Outcome(ticket.Id, "confirmed", Owner))).ShouldContain("execution.reason_required");
        await ExecDatabase.ExecuteAsync(db.Exec, $"INSERT INTO execution.halts (at, scope, account_id, level, trigger_code, source, actor, reason) VALUES (now(), 'account', '{halted.Account}', 'entries', 'daily_loss', 'system', 'system:halts', 'r')");
        (await RefusalAsync(db.App, Outcome(haltedTicket.Id, "confirmed", Owner))).ShouldContain("execution.halted");

        (await RefusalAsync(db.App, Outcome(ticket.Id, "confirmed", Owner, "low volume known"))).ShouldBeEmpty();

        (await RefusalAsync(db.App, Outcome(ticket.Id, "confirmed", Owner, "again"))).ShouldStartWith("23505");
        (await RefusalAsync(db.App, Outcome(ticket.Id, "skipped", Owner))).ShouldStartWith("23505");
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT concat_ws('|', c.actor, c.reason, array_to_string(c.codes, ',')) FROM execution.check_confirmations c JOIN execution.confirm_tickets t ON t.check_run_id = c.run_id WHERE t.id = '{ticket.Id}'"))
            .ShouldBe($"{Owner}|low volume known|adv_share");
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT decided_at <= clock_timestamp() AND decided_at > clock_timestamp() - interval '1 minute' FROM execution.ticket_outcomes WHERE ticket_id = '{ticket.Id}'")).ShouldBe(true);
        (await RefusalAsync(db.App, $"INSERT INTO execution.mode_trail (account_id, from_mode, to_mode, kind, actor, reason, gate_snapshot, gate_hash, effective_from) VALUES ('{s.Account}', 'semi_auto', 'auto_live', 'owner_demotion', '{Owner}', 'r', '{{}}', repeat('b', 64), now())"))
            .ShouldStartWith("23514", customMessage: "P12-07 holds: no new row can name semi_auto or auto_live");
    }

    [Fact]
    public async Task After_the_expiry_by_the_database_clock_a_tap_or_a_skip_is_refused_and_only_the_execution_service_closes_it_expired()
    {
        var s = await SeedAsync();
        await SwitchAsync(true);
        await SemiAutoAsync(s.Account);
        await UnlockAsync(s);
        var late = await OldTicketAsync(s, 91);
        var fresh = await OldTicketAsync(await SeedAsync(), 0);

        (await RefusalAsync(db.App, Outcome(late, "confirmed", Owner))).ShouldContain("execution.ticket_expired");
        (await RefusalAsync(db.App, Outcome(late, "skipped", Owner))).ShouldContain("execution.ticket_expired");
        (await RefusalAsync(db.App, Outcome(late, "expired", TicketOutcomes.ExpiryActor))).ShouldContain("execution service");
        (await RefusalAsync(db.Exec, Outcome(fresh, "expired", TicketOutcomes.ExpiryActor))).ShouldContain("only at or after its expiry");

        await using var data = NpgsqlDataSource.Create(db.Exec);
        var closed = await new PgConfirmTickets(data).ExpireDueAsync(TicketOutcomes.ExpiryActor, Ct);

        closed.ShouldContain(c => c.TicketId == late && c.Outcome == TicketOutcomes.Expired && c.MillisFromIssue >= 90_000);
        closed.ShouldNotContain(c => c.TicketId == fresh);
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT concat_ws('|', outcome, reason, actor) FROM execution.ticket_outcomes WHERE ticket_id = '{late}'")).ShouldBe($"expired|ttl|{TicketOutcomes.ExpiryActor}");
    }

    [Fact]
    public async Task A_tap_just_before_the_expiry_passes_and_one_at_the_expiry_is_refused_by_the_database_clock()
    {
        var s = await SeedAsync();
        await SwitchAsync(true);
        await SemiAutoAsync(s.Account);
        await UnlockAsync(s);
        var passes = await OldTicketAsync(s, 88.5);
        var other = await SeedAsync();
        await SemiAutoAsync(other.Account);
        await UnlockAsync(other);
        var refused = await OldTicketAsync(other, 88.5);

        (await RefusalAsync(db.App, Outcome(passes, "confirmed", Owner))).ShouldBeEmpty();
        await Task.Delay(TimeSpan.FromSeconds(1.6), Ct);
        (await RefusalAsync(db.App, Outcome(refused, "confirmed", Owner))).ShouldContain("execution.ticket_expired");
    }

    [Fact]
    public async Task Twenty_parallel_taps_through_the_api_handler_leave_one_row_one_first_answer_and_nineteen_already_decided_with_the_same_time()
    {
        var s = await SeedAsync();
        var ticket = await IssueAsync(s);
        await SwitchAsync(true);
        await SemiAutoAsync(s.Account);
        await UnlockAsync(s);
        await ReadyAsync(s);
        await QuoteAsync(s, 412.31m);
        var hash = ticket.TextHash;

        var answers = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var context = AppContext();
            var handler = new ConfirmTicketHandler(new ConfirmTicketAccess(context), new NoCommands(), new NoPassword(), NullLogger<ConfirmTicketHandler>.Instance);
            return await handler.HandleAsync(new ConfirmTicketCommand(Owner, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), ticket.Id, hash, null, null), Ct);
        }));

        answers.ShouldAllBe(a => a.IsSuccess);
        answers.Count(a => !a.Value.AlreadyDecided).ShouldBe(1);
        answers.Count(a => a.Value.AlreadyDecided).ShouldBe(19);
        answers.Select(a => a.Value.DecidedAt).Distinct().Count().ShouldBe(1);
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT count(*) FROM execution.ticket_outcomes WHERE ticket_id = '{ticket.Id}' AND outcome = 'confirmed'")).ShouldBe(1L);

        await using var data = NpgsqlDataSource.Create(db.Exec);
        (await new StoredConfirmGate(new PgConfirmTickets(data)).CheckAsync(s.IntentId, Ct)).ShouldBe(new GateDecision(true, TicketOutcomes.Confirmed));
    }

    [Fact]
    public async Task A_changed_text_through_the_api_handler_stores_nothing_and_the_list_shows_the_ticket_with_the_database_clock()
    {
        var s = await SeedAsync();
        var ticket = await IssueAsync(s);
        await using var context = AppContext();
        var access = new ConfirmTicketAccess(context);
        var handler = new ConfirmTicketHandler(access, new NoCommands(), new NoPassword(), NullLogger<ConfirmTicketHandler>.Instance);

        var result = await handler.HandleAsync(new ConfirmTicketCommand(Owner, Guid.NewGuid(), new ClientInfo("127.0.0.1", "test"), ticket.Id, new string('0', 64), null, null), Ct);
        var listed = (await new GetConfirmTicketsHandler(access).HandleAsync(new GetConfirmTicketsQuery("open", s.Session), Ct)).Value.Single(v => v.Id == ticket.Id);

        result.Error.Code.ShouldBe(ConfirmCodes.TextChanged);
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT count(*) FROM execution.ticket_outcomes WHERE ticket_id = '{ticket.Id}'")).ShouldBe(0L);
        (listed.State, listed.Text, listed.TextHash).ShouldBe((TicketOutcomes.Open, ticket.Text, ticket.TextHash));
        listed.ServerNow.ShouldBeGreaterThanOrEqualTo(listed.IssuedAt);
        listed.AccountName.ShouldStartWith("Confirm ");
    }

    [Fact]
    public async Task A_tap_racing_the_expiry_job_leaves_exactly_one_row()
    {
        var s = await SeedAsync();
        await SwitchAsync(true);
        await SemiAutoAsync(s.Account);
        await UnlockAsync(s);
        var ticket = await OldTicketAsync(s, 89.7);
        await using var data = NpgsqlDataSource.Create(db.Exec);
        var store = new PgConfirmTickets(data);

        for (var i = 0; i < 40 && (long)(await ExecDatabase.ScalarAsync(db.Owner, $"SELECT count(*) FROM execution.ticket_outcomes WHERE ticket_id = '{ticket}'"))! == 0; i++)
        {
            await Task.WhenAll(RefusalAsync(db.App, Outcome(ticket, "confirmed", Owner)), store.ExpireDueAsync(TicketOutcomes.ExpiryActor, Ct));
            await Task.Delay(TimeSpan.FromMilliseconds(50), Ct);
        }

        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT count(*) FROM execution.ticket_outcomes WHERE ticket_id = '{ticket}'")).ShouldBe(1L);
        (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT outcome FROM execution.ticket_outcomes WHERE ticket_id = '{ticket}'")).ShouldBeOneOf("confirmed", "expired");
    }

    [Fact]
    public async Task A_day_unlock_is_the_owners_for_today_only_stamped_by_the_database_and_valid_until_15_30_india_time()
    {
        var s = await SeedAsync();
        var insert = "INSERT INTO execution.day_unlocks (account_id, session_date, expires_at, actor) VALUES ('{0}', '{1:yyyy-MM-dd}', now(), '{2}')";

        (await RefusalAsync(db.App, string.Format(CultureInfo.InvariantCulture, insert, s.Account, s.Session.AddDays(-1), Owner))).ShouldContain("execution.unlock_closed");
        (await RefusalAsync(db.App, string.Format(CultureInfo.InvariantCulture, insert, s.Account, s.Session.AddDays(1), Owner))).ShouldContain("execution.unlock_closed");
        (await RefusalAsync(db.App, string.Format(CultureInfo.InvariantCulture, insert, s.Account, s.Session, "system:x"))).ShouldStartWith("23514");
        (await RefusalAsync(db.Exec, string.Format(CultureInfo.InvariantCulture, insert, s.Account, s.Session, Owner))).ShouldStartWith("42501");

        var beforeClose = (bool)(await ExecDatabase.ScalarAsync(db.Owner, "SELECT (clock_timestamp() AT TIME ZONE 'Asia/Kolkata')::time < time '15:30'"))!;
        var today = await RefusalAsync(db.App, string.Format(CultureInfo.InvariantCulture, insert, s.Account, s.Session, Owner));
        if (beforeClose)
        {
            today.ShouldBeEmpty();
            (await ExecDatabase.ScalarAsync(db.Owner, $"SELECT expires_at = ('{s.Session:yyyy-MM-dd}'::date + time '15:30') AT TIME ZONE 'Asia/Kolkata' AND unlocked_at > now() - interval '1 minute' FROM execution.day_unlocks WHERE account_id = '{s.Account}'"))
                .ShouldBe(true);
        }
        else
        {
            today.ShouldContain("execution.unlock_closed");
        }

        foreach (var statement in new[] { "UPDATE execution.day_unlocks SET actor = 'owner:y'", "DELETE FROM execution.day_unlocks" })
        {
            (await ExecDatabase.SqlStateAsync(db.App, statement)).ShouldBe("42501", statement);
        }
    }

    [Fact]
    public async Task Issuing_and_deciding_a_ticket_notify_the_execution_ticket_channel()
    {
        var s = await SeedAsync();
        await using var listener = new NpgsqlConnection(db.Owner);
        await listener.OpenAsync(Ct);
        var heard = 0;
        listener.Notification += (_, e) =>
        {
            if (e.Channel == "execution_ticket")
            {
                Interlocked.Increment(ref heard);
            }
        };
        await using (var listen = new NpgsqlCommand("LISTEN execution_ticket", listener))
        {
            await listen.ExecuteNonQueryAsync(Ct);
        }

        var ticket = await IssueAsync(s);
        (await RefusalAsync(db.App, Outcome(ticket.Id, "skipped", Owner, "not today"))).ShouldBeEmpty();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref heard) < 2 && DateTimeOffset.UtcNow < deadline)
        {
            await listener.WaitAsync(TimeSpan.FromMilliseconds(200), Ct);
        }

        heard.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task The_execution_role_reads_only_the_capital_columns_of_the_trading_settings()
    {
        (await ExecDatabase.SqlStateAsync(db.Exec, "SELECT account_id, capital_source, capital_amount FROM accounts.trading_settings")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, "SELECT * FROM accounts.trading_settings")).ShouldBe("42501");
        (await ExecDatabase.SqlStateAsync(db.Exec, "UPDATE accounts.trading_settings SET capital_amount = 1")).ShouldBe("42501");
    }
}
