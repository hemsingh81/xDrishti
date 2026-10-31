using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Execution;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>
/// The records of the P12 phase check (spec P12-12) as the runtime role: SELECT only on the execution schema and the Trading accounts. No table, no migration, no grant; nothing is written. Each list is bounded by the window of at most 200 sessions. Nothing here
/// selects a token, a client id, a secret or a balance.
/// </summary>
internal sealed class ExecutionRecordsReader(XDrishtiDbContext db) : IExecutionRecordsReader
{
    private const string Ist = "AT TIME ZONE 'Asia/Kolkata'";

    public async ValueTask<IReadOnlyList<PhaseAccount>> AccountsAsync(Guid? accountId, CancellationToken cancellationToken) =>
        await ExecutionConnection.UseAsync<IReadOnlyList<PhaseAccount>>(
            db,
            c => ListAsync(
                c,
                "SELECT a.id, a.name FROM accounts.broker_accounts a JOIN accounts.trading_settings t ON t.account_id = a.id WHERE a.archived_at IS NULL AND t.enabled AND (@a::uuid IS NULL OR a.id = @a) ORDER BY a.name, a.id",
                r => new PhaseAccount(r.GetGuid(0), r.GetString(1)), cancellationToken, ("a", NpgsqlDbType.Uuid, accountId)),
            cancellationToken);

    public async ValueTask<IReadOnlyList<PhaseTrailRow>> TrailAsync(Guid accountId, CancellationToken cancellationToken) =>
        await ExecutionConnection.UseAsync<IReadOnlyList<PhaseTrailRow>>(
            db,
            c => ListAsync(
                c, "SELECT effective_from, from_mode, to_mode, kind FROM execution.mode_trail WHERE account_id = @a ORDER BY effective_from, id",
                r => new PhaseTrailRow(r.GetFieldValue<DateTimeOffset>(0), r.GetString(1), r.GetString(2), r.GetString(3)), cancellationToken, ("a", NpgsqlDbType.Uuid, accountId)),
            cancellationToken);

    public async ValueTask<IReadOnlyList<PhaseSettingsRow>> SettingsAsync(CancellationToken cancellationToken) =>
        await ExecutionConnection.UseAsync<IReadOnlyList<PhaseSettingsRow>>(
            db,
            c => ListAsync(
                c, "SELECT effective_from, \"values\" ->> 'exec.canary_pending' FROM execution.settings ORDER BY effective_from, id",
                r => new PhaseSettingsRow(r.GetFieldValue<DateTimeOffset>(0), !r.IsDBNull(1) && bool.TryParse(r.GetString(1), out var pending) ? pending : null), cancellationToken),
            cancellationToken);

    public async ValueTask<DateOnly?> NewestSessionAsync(CancellationToken cancellationToken) =>
        await ExecutionConnection.UseAsync<DateOnly?>(
            db,
            async c =>
            {
                await using var command = new NpgsqlCommand(
                    "SELECT max(d) FROM (SELECT max(session_date) AS d FROM execution.intents UNION ALL SELECT max(session_date) FROM execution.recon_runs UNION ALL SELECT max(session_date) FROM execution.day_states) x", c);
                var value = await command.ExecuteScalarAsync(cancellationToken);
                return value is DateOnly d ? d : null;
            },
            cancellationToken);

    public async ValueTask<PhaseAccountRecords> RecordsAsync(Guid accountId, DateOnly from, DateOnly through, CancellationToken cancellationToken) =>
        await ExecutionConnection.UseAsync(
            db,
            async c =>
            {
                (string, NpgsqlDbType, object?)[] p = [("a", NpgsqlDbType.Uuid, accountId), ("f", NpgsqlDbType.Date, from), ("t", NpgsqlDbType.Date, through)];
                var entries = await ListAsync(
                    c, $"""
                    SELECT i.session_date, i.environment,
                           EXISTS (SELECT 1 FROM execution.confirm_tickets t JOIN execution.ticket_outcomes o ON o.ticket_id = t.id
                                   WHERE t.intent_public_id = i.public_id AND o.outcome = 'confirmed' AND o.actor LIKE 'owner:%'
                                     AND o.decided_at <= l.send_started_at AND o.decided_at >= l.send_started_at - interval '30 seconds')
                    FROM execution.legs l JOIN execution.intents i ON i.id = l.intent_id
                    WHERE i.account_id = @a AND i.session_date BETWEEN @f AND @t AND l.kind = 'E' AND l.send_started_at IS NOT NULL
                    """,
                    r => new PhaseEntryRow(r.GetFieldValue<DateOnly>(0), r.GetString(1), r.GetBoolean(2)), cancellationToken, p);
                var sandbox = await ListAsync(c, "SELECT session_date FROM execution.intents WHERE account_id = @a AND environment = 'sandbox' AND session_date BETWEEN @f AND @t", r => r.GetFieldValue<DateOnly>(0), cancellationToken, p);
                var tickets = await ListAsync(
                    c, "SELECT t.session_date, o.outcome, t.issued_at, o.decided_at FROM execution.confirm_tickets t LEFT JOIN execution.ticket_outcomes o ON o.ticket_id = t.id WHERE t.account_id = @a AND t.session_date BETWEEN @f AND @t",
                    r => new PhaseTicketRow(r.GetFieldValue<DateOnly>(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetFieldValue<DateTimeOffset>(2), r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3)), cancellationToken, p);
                var runs = await ListAsync(c, "SELECT session_date, kind, readable FROM execution.recon_runs WHERE account_id = @a AND session_date BETWEEN @f AND @t", r => new PhaseRunRow(r.GetFieldValue<DateOnly>(0), r.GetString(1), r.GetBoolean(2)), cancellationToken, p);
                var breaks = await ListAsync(c, "SELECT session_date, kind FROM execution.recon_breaks WHERE account_id = @a AND session_date BETWEEN @f AND @t", r => new PhaseBreakRow(r.GetFieldValue<DateOnly>(0), r.GetString(1)), cancellationToken, p);
                var alarms = await ListAsync(
                    c, $"SELECT (at {Ist})::date, code FROM execution.alarms WHERE (account_id = @a OR account_id IS NULL) AND (at {Ist})::date BETWEEN @f AND @t", r => new PhaseCodeRow(r.GetFieldValue<DateOnly>(0), r.GetString(1)), cancellationToken, p);
                var halts = await ListAsync(
                    c, $"SELECT (at {Ist})::date, trigger_code FROM execution.halts WHERE (account_id = @a OR account_id IS NULL) AND (at {Ist})::date BETWEEN @f AND @t", r => new PhaseCodeRow(r.GetFieldValue<DateOnly>(0), r.GetString(1)), cancellationToken, p);
                var states = await ListAsync(
                    c, "SELECT DISTINCT ON (session_date) session_date, state FROM execution.day_states WHERE account_id = @a AND session_date BETWEEN @f AND @t ORDER BY session_date, at, id",
                    r => new PhaseDayStateRow(r.GetFieldValue<DateOnly>(0), r.GetString(1)), cancellationToken, p);
                var carried = await ListAsync(
                    c, $"""
                    SELECT i.session_date, CASE WHEN f.eq <> COALESCE(f.xq, 0) THEN NULL ELSE f.xd END
                    FROM execution.intents i
                    JOIN (SELECT x.intent_id, SUM(x.qty) FILTER (WHERE l.kind = 'E') AS eq, SUM(x.qty) FILTER (WHERE l.kind <> 'E') AS xq,
                                 max((x.at {Ist})::date) FILTER (WHERE l.kind <> 'E') AS xd
                          FROM execution.fills x JOIN execution.legs l ON l.id = x.leg_id GROUP BY x.intent_id) f ON f.intent_id = i.id
                    WHERE i.account_id = @a AND i.environment = 'live' AND i.session_date <= @t AND f.eq > 0
                      AND ((f.eq <> COALESCE(f.xq, 0)) OR (f.xd > i.session_date AND f.xd >= @f))
                    """,
                    r => new PhaseCarriedRow(r.GetFieldValue<DateOnly>(0), r.IsDBNull(1) ? null : r.GetFieldValue<DateOnly>(1)), cancellationToken, p);
                var drills = await ListAsync(c, $"SELECT (at {Ist})::date, passed FROM execution.drills WHERE account_id = @a AND (at {Ist})::date BETWEEN @f AND @t ORDER BY at, id", r => new PhaseDrillRow(r.GetFieldValue<DateOnly>(0), r.GetBoolean(1)), cancellationToken, p);
                var legs = await ListAsync(
                    c, "SELECT count(*)::int, (count(*) FILTER (WHERE l.algo_id IS NOT NULL))::int FROM execution.legs l JOIN execution.intents i ON i.id = l.intent_id WHERE i.account_id = @a AND i.environment = 'live' AND i.session_date BETWEEN @f AND @t AND l.send_started_at IS NOT NULL",
                    r => (r.GetInt32(0), r.GetInt32(1)), cancellationToken, p);
                var fills = await ListAsync(
                    c, "SELECT count(*)::int FROM execution.fills x JOIN execution.legs l ON l.id = x.leg_id JOIN execution.intents i ON i.id = x.intent_id WHERE i.account_id = @a AND i.environment = 'live' AND i.session_date BETWEEN @f AND @t AND l.kind = 'E'",
                    r => r.GetInt32(0), cancellationToken, p);
                return new PhaseAccountRecords(entries, tickets, runs, breaks, alarms, halts, states, carried, drills, legs[0].Item1, legs[0].Item2, fills[0], sandbox);
            },
            cancellationToken);

    private static async Task<IReadOnlyList<T>> ListAsync<T>(NpgsqlConnection connection, string sql, Func<NpgsqlDataReader, T> map, CancellationToken cancellationToken, params (string Name, NpgsqlDbType Type, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, type, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        }

        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
