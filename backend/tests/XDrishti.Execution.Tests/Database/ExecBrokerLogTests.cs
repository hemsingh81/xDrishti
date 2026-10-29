using Microsoft.Extensions.Time.Testing;
using Npgsql;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Persistence;

namespace XDrishti.Execution.Tests.Database;

/// <summary>P12-02b: the broker-call audit table (insert-only, codes only), the persistent write-ahead journal, the restart-proof counts and the token gate, on a dedicated server, as the role xd_exec.</summary>
[Collection(ExecDatabaseSuite.Name)]
public sealed class ExecBrokerLogTests(ExecDatabase db)
{
    private static readonly DateOnly Day = new(2026, 10, 29);
    private static long _next = 5000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private NpgsqlDataSource Data() => NpgsqlDataSource.Create(db.Exec);

    private async Task<long> NewIntentAsync(NpgsqlDataSource data)
    {
        var id = Interlocked.Increment(ref _next);
        var intent = ExecIntent.Create(id, Guid.NewGuid(), db.AccountId, id, ExecSide.Buy, "TCS", "11536", 10, 100m, 98m, 104m, ExecStructure.Super).Value;
        (await new PgExecEventStore(data).StartAsync(intent, Day, ExecEnvironment.Sandbox, "system:test", Ct)).IsSuccess.ShouldBeTrue();
        return id;
    }

    private static BrokerLeg LegOf(long intentId, LegKind kind = LegKind.Entry, int attempt = 1) =>
        new(ClientOrderId.Create(Day, intentId, kind, attempt).Value.ToString(), kind, ExecSide.Buy, "11536", 10, 100m);

    private async Task<long> CountAsync(string sql) => Convert.ToInt64(await ExecDatabase.ScalarAsync(db.Owner, sql), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_audit_table_is_insert_only_for_everyone_including_the_owner()
    {
        await using var data = Data();
        var leg = LegOf(await NewIntentAsync(data));
        await new PgSendJournal(data).BeginAsync(db.AccountId, ParseIntent(leg), leg, Ct);

        foreach (var statement in new[] { "UPDATE execution.broker_calls SET reason = 'x'", "DELETE FROM execution.broker_calls", "TRUNCATE execution.broker_calls" })
        {
            (await ExecDatabase.SqlStateAsync(db.Owner, statement)).ShouldBe("23514", "owner: " + statement);
            (await ExecDatabase.SqlStateAsync(db.Exec, statement)).ShouldBe("42501", "xd_exec: " + statement);
            (await ExecDatabase.SqlStateAsync(db.App, statement)).ShouldBe("42501", "xd_app: " + statement);
        }

        (await ExecDatabase.SqlStateAsync(db.App, "SELECT count(*) FROM execution.broker_calls")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.App, $"INSERT INTO execution.broker_calls (account_id, call, phase, environment) VALUES ('{db.AccountId}', 'orders_post', 'begin', 'sandbox')")).ShouldBe("42501");
    }

    private static long ParseIntent(BrokerLeg leg) => ClientOrderId.TryParse(leg.ClientOrderId, out var id) ? id.IntentId : 0;

    [Theory]
    [InlineData("begin", "NULL", "'with a space'", "NULL", "sandbox")]
    [InlineData("outcome", "'unknown'", "'Bearer SECRET'", "NULL", "sandbox")]
    [InlineData("outcome", "'unknown'", "'a-b'", "NULL", "sandbox")]
    [InlineData("outcome", "'unknown'", "'x'", "'has space'", "sandbox")]
    [InlineData("outcome", "'unknown'", "'x'", "NULL", "live")]
    [InlineData("outcome", "'unknown'", "'x'", "NULL", "none")]
    [InlineData("outcome", "'maybe'", "'x'", "NULL", "sandbox")]
    [InlineData("outcome", "NULL", "'x'", "NULL", "sandbox")]
    [InlineData("dropped", "NULL", "NULL", "NULL", "sandbox")]
    [InlineData("begin", "'unknown'", "NULL", "NULL", "sandbox")]
    [InlineData("other", "NULL", "NULL", "NULL", "sandbox")]
    public async Task The_table_refuses_free_text_a_live_environment_and_every_malformed_row(string phase, string outcome, string reason, string brokerId, string environment) =>
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.broker_calls (account_id, call, phase, environment, outcome, reason, broker_order_id) VALUES ('{db.AccountId}', 'orders_post', '{phase}', '{environment}', {outcome}, {reason}, {brokerId})"))
            .ShouldBe("23514");

    [Fact]
    public async Task A_well_formed_row_of_each_phase_is_accepted_and_a_call_other_than_the_one_built_is_not()
    {
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.broker_calls (account_id, call, phase, environment) VALUES ('{db.AccountId}', 'orders_post', 'begin', 'sandbox')")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.broker_calls (account_id, call, phase, environment, reason) VALUES ('{db.AccountId}', 'orders_post', 'dropped', 'sandbox', 'halted_before_send')")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.broker_calls (account_id, call, phase, environment, outcome, reason, broker_order_id) VALUES ('{db.AccountId}', 'orders_post', 'outcome', 'sandbox', 'acknowledged', 'pending', 'B-1_x')")).ShouldBeNull();
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.broker_calls (account_id, call, phase, environment) VALUES ('{db.AccountId}', 'orders_delete', 'begin', 'sandbox')")).ShouldBe("23514");
        (await ExecDatabase.SqlStateAsync(db.Exec, $"INSERT INTO execution.broker_calls (account_id, call, phase, environment) VALUES ('{Guid.NewGuid()}', 'orders_post', 'begin', 'sandbox')")).ShouldBe("23503");
    }

    [Fact]
    public async Task The_write_ahead_commits_the_leg_and_the_begin_row_together_before_the_outcome_and_a_second_begin_of_the_same_id_stores_nothing()
    {
        await using var data = Data();
        var journal = new PgSendJournal(data);
        var intent = await NewIntentAsync(data);
        var leg = LegOf(intent);

        await journal.BeginAsync(db.AccountId, intent, leg, Ct);

        (await CountAsync($"SELECT count(*) FROM execution.legs WHERE client_order_id = '{leg.ClientOrderId}' AND status = 'new' AND send_started_at IS NOT NULL")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM execution.broker_calls WHERE client_order_id = '{leg.ClientOrderId}' AND phase = 'begin' AND intent_id = {intent}")).ShouldBe(1);

        await Should.ThrowAsync<PostgresException>(() => journal.BeginAsync(db.AccountId, intent, leg, Ct));
        (await CountAsync($"SELECT count(*) FROM execution.broker_calls WHERE client_order_id = '{leg.ClientOrderId}'")).ShouldBe(1);
        await Should.ThrowAsync<InvalidOperationException>(() => journal.BeginAsync(Guid.NewGuid(), intent, LegOf(intent, attempt: 2), Ct));
        (await CountAsync($"SELECT count(*) FROM execution.legs WHERE intent_id = {intent}")).ShouldBe(1);

        await journal.OutcomeAsync(leg, new BrokerAck(BrokerOutcome.Acknowledged, "112111182198", "PENDING"), Ct);

        (await CountAsync($"SELECT count(*) FROM execution.legs WHERE client_order_id = '{leg.ClientOrderId}' AND status = 'transit' AND broker_order_id = '112111182198' AND acked_at IS NOT NULL")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM execution.broker_calls WHERE client_order_id = '{leg.ClientOrderId}' AND phase = 'outcome' AND outcome = 'acknowledged' AND reason = 'pending'")).ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_and_not_sent_and_dropped_are_recorded_as_codes_and_free_text_becomes_a_fixed_code()
    {
        await using var data = Data();
        var journal = new PgSendJournal(data);
        var a = LegOf(await NewIntentAsync(data));
        var b = LegOf(await NewIntentAsync(data));
        var c = LegOf(await NewIntentAsync(data));
        foreach (var leg in new[] { a, b, c })
        {
            await journal.BeginAsync(db.AccountId, ParseIntent(leg), leg, Ct);
        }

        await journal.OutcomeAsync(a, new BrokerAck(BrokerOutcome.Unknown, null, "Bearer SECRET-TOKEN-VALUE for client 1000000001"), Ct);
        await journal.OutcomeAsync(b, new BrokerAck(BrokerOutcome.NotSent, "not a token!", "ip_not_allowed"), Ct);
        await journal.DroppedAsync(c, "halted_before_send", Ct);

        (await CountAsync($"SELECT count(*) FROM execution.legs WHERE client_order_id = '{a.ClientOrderId}' AND status = 'sent' AND broker_order_id IS NULL")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM execution.broker_calls WHERE client_order_id = '{a.ClientOrderId}' AND outcome = 'unknown' AND reason = 'reason_invalid'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM execution.legs WHERE client_order_id = '{b.ClientOrderId}' AND status = 'rejected' AND broker_order_id IS NULL")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM execution.broker_calls WHERE client_order_id = '{b.ClientOrderId}' AND outcome = 'not_sent' AND reason = 'ip_not_allowed' AND broker_order_id IS NULL")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM execution.legs WHERE client_order_id = '{c.ClientOrderId}' AND status = 'cancelled'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM execution.broker_calls WHERE client_order_id = '{c.ClientOrderId}' AND phase = 'dropped' AND reason = 'halted_before_send'")).ShouldBe(1);

        // a scan of the whole table: nothing token-like, no client id of the broker account, no header
        (await CountAsync("SELECT count(*) FROM execution.broker_calls WHERE (coalesce(reason, '') || coalesce(broker_order_id, '') || coalesce(client_order_id, '')) ~* '(secret|bearer|token|access|1000000001)' AND coalesce(reason, '') <> 'token_unavailable'")).ShouldBe(0);
    }

    [Fact]
    public async Task The_begin_times_come_back_in_order_and_a_new_instance_and_a_new_limiter_keep_the_day_count()
    {
        await using var data = Data();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var broker = new FakeBroker(clock);
        var before = await new PgSendJournal(data).BeginsSinceAsync(DateTimeOffset.UtcNow.AddDays(-1), Ct);

        LegSender Sender(PgSendJournal journal, SendSettings settings) => new(
            broker, new Modes(), new Halts(), new Open(), journal, new ExecRateLimiter(clock, 99, 99, 99), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(99),
            new Settings(settings), new Token(), journal);

        var settings = new SendSettings(99, 99, before.Count + 3, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), 1.0m, 20);
        var first = Sender(new PgSendJournal(data), settings);
        for (var i = 0; i < 3; i++)
        {
            var intent = await NewIntentAsync(data);
            (await first.SendAsync(new SendRequest(db.AccountId, intent, LegOf(intent), clock.GetUtcNow()), Ct)).Status.ShouldBe(SendStatus.Sent);
        }

        var times = await new PgSendJournal(data).BeginsSinceAsync(DateTimeOffset.UtcNow.AddDays(-1), Ct);
        times.Count.ShouldBe(before.Count + 3);
        times.ShouldBe([.. times.Order()]);
        (await new PgSendJournal(data).BeginsSinceAsync(DateTimeOffset.UtcNow.AddMinutes(5), Ct)).ShouldBeEmpty();

        // the process restarts: a new journal object, a new sender, a new empty limiter. The day cap still holds and nothing reaches the broker or the table.
        var calls = broker.Calls;
        var restarted = Sender(new PgSendJournal(data), settings);
        var next = await NewIntentAsync(data);
        var refused = await restarted.SendAsync(new SendRequest(db.AccountId, next, LegOf(next), clock.GetUtcNow()), Ct);

        (refused.Status, refused.Reason).ShouldBe((SendStatus.Refused, "rate_limited"));
        broker.Calls.ShouldBe(calls);
        (await CountAsync($"SELECT count(*) FROM execution.legs WHERE intent_id = {next}")).ShouldBe(0);
    }

    [Fact]
    public async Task The_broker_is_called_only_after_the_begin_row_is_committed()
    {
        await using var data = Data();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var seenAtPlace = -1L;
        var intent = await NewIntentAsync(data);
        var leg = LegOf(intent);
        var spy = new Spy(async l => seenAtPlace = await CountAsync($"SELECT count(*) FROM execution.broker_calls WHERE client_order_id = '{l.ClientOrderId}' AND phase = 'begin'"));
        var journal = new PgSendJournal(data);
        var sender = new LegSender(spy, new Modes(), new Halts(), new Open(), journal, new ExecRateLimiter(clock, 99, 99, 99), ExecEnvironment.Sandbox, clock, TimeSpan.FromSeconds(99),
            new Settings(new SendSettings(99, 99, 99999, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), 1.0m, 20)), new Token(), journal);

        (await sender.SendAsync(new SendRequest(db.AccountId, intent, leg, clock.GetUtcNow()), Ct)).Status.ShouldBe(SendStatus.Sent);

        seenAtPlace.ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM execution.broker_calls WHERE client_order_id = '{leg.ClientOrderId}'")).ShouldBe(2);
    }

    [Fact]
    public async Task The_token_gate_reads_the_stored_connection_and_every_gap_is_unavailable()
    {
        await using var data = Data();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new PgTokenGate(data, clock);
        var account = await NewAccountAsync();

        (await gate.CheckAsync(account, 20, Ct)).Reason.ShouldBe("token_unavailable");
        await ExecDatabase.ExecuteAsync(db.Owner, $"INSERT INTO accounts.broker_connections (account_id, status, expires_at) VALUES ('{account}', 'Connected', now() + interval '3 hours')");
        (await gate.CheckAsync(account, 20, Ct)).Open.ShouldBeTrue();
        await ExecDatabase.ExecuteAsync(db.Owner, $"UPDATE accounts.broker_connections SET expires_at = now() + interval '10 minutes' WHERE account_id = '{account}'");
        (await gate.CheckAsync(account, 20, Ct)).Reason.ShouldBe("token_expiring");
        await ExecDatabase.ExecuteAsync(db.Owner, $"UPDATE accounts.broker_connections SET expires_at = now() - interval '1 minute' WHERE account_id = '{account}'");
        (await gate.CheckAsync(account, 20, Ct)).Reason.ShouldBe("token_expired");
        await ExecDatabase.ExecuteAsync(db.Owner, $"UPDATE accounts.broker_connections SET status = 'NotConnected', expires_at = now() + interval '3 hours' WHERE account_id = '{account}'");
        (await gate.CheckAsync(account, 20, Ct)).Reason.ShouldBe("token_unavailable");
        await ExecDatabase.ExecuteAsync(db.Owner, $"UPDATE accounts.broker_connections SET status = 'Connected', expires_at = NULL WHERE account_id = '{account}'");
        (await gate.CheckAsync(account, 20, Ct)).Reason.ShouldBe("token_unavailable");

        await using var broken = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(db.Exec) { Password = "wrong", Pooling = false }.ConnectionString);
        (await new PgTokenGate(broken, clock).CheckAsync(account, 20, Ct)).Reason.ShouldBe("token_unavailable");
    }

    private async Task<Guid> NewAccountAsync()
    {
        var id = Guid.NewGuid();
        await ExecDatabase.ExecuteAsync(
            db.Owner,
            $"INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget) VALUES ('{id}', 'a-{id.ToString("N")[..8]}', 'x', 'Dhan', '{Random.Shared.NextInt64(2000000000, 2999999999)}', current_date, '#112233', now(), now(), false, false, false, 0)");
        return id;
    }

    private sealed class Spy(Func<BrokerLeg, Task> onPlace) : IBrokerOrders
    {
        public async Task<BrokerAck> PlaceAsync(BrokerLeg leg, CancellationToken cancellationToken)
        {
            await onPlace(leg);
            return new BrokerAck(BrokerOutcome.Acknowledged, "B1");
        }

        public Task<BrokerAck> ModifyAsync(string clientOrderId, decimal price, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrokerAck> CancelAsync(string clientOrderId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrokerLeg?> LookupAsync(string clientOrderId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Modes : IModeSource
    {
        public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult<ModeAnswer?>(new ModeAnswer(ExecMode.SemiAuto, true));
    }

    private sealed class Halts : IHaltSource
    {
        public Task<HaltReading> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(HaltReading.Clear);
    }

    private sealed class Open : IPreSendCheck
    {
        public Task<GateDecision> CheckAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) => Task.FromResult(GateDecision.Allowed);
    }

    private sealed class Settings(SendSettings value) : ISendSettings
    {
        public SendSettings? Current() => value;
    }

    private sealed class Token : ITokenGate
    {
        public Task<GateDecision> CheckAsync(Guid accountId, int minRemainingMinutes, CancellationToken cancellationToken) => Task.FromResult(GateDecision.Allowed);
    }
}
