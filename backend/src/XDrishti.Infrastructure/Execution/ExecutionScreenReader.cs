using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Execution;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>
/// The execution screens' reads (spec P12-10) as the runtime role: SELECT only on schema <c>execution</c>. Every list is bounded (a page, or at most 100 rows). Nothing here selects a token, a client id, a secret or a balance; the broker account is read for its
/// display name only.
/// </summary>
internal sealed class ExecutionScreenReader(XDrishtiDbContext db) : IExecutionScreenReader
{
    public Task<IReadOnlyList<DayAccountRaw>> DayAsync(DateOnly date, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<DayAccountRaw>>(db, async connection =>
        {
            var intents = new Dictionary<Guid, (int Intents, int Filled, decimal Risk, bool Inconsistent)>();
            await using (var command = Command(connection, """
                WITH f AS (
                    SELECT l.intent_id,
                           COALESCE(SUM(x.qty) FILTER (WHERE l.kind = 'E'), 0) AS eq,
                           COALESCE(SUM(x.qty * x.price) FILTER (WHERE l.kind = 'E'), 0) AS ev,
                           COALESCE(SUM(x.qty) FILTER (WHERE l.kind <> 'E'), 0) AS xq
                    FROM execution.fills x JOIN execution.legs l ON l.id = x.leg_id
                    WHERE l.intent_id IN (SELECT id FROM execution.intents WHERE session_date = @d)
                    GROUP BY l.intent_id)
                SELECT i.account_id, count(*)::int, (count(*) FILTER (WHERE f.eq > 0))::int,
                       COALESCE(SUM(CASE WHEN f.eq > f.xq THEN GREATEST(0, CASE WHEN i.side = 'buy' THEN f.ev / f.eq - i.stop_price ELSE i.stop_price - f.ev / f.eq END) * (f.eq - f.xq) ELSE 0 END), 0),
                       COALESCE(bool_or(f.xq > f.eq), false)
                FROM execution.intents i LEFT JOIN f ON f.intent_id = i.id
                WHERE i.session_date = @d GROUP BY i.account_id
                """, ("d", NpgsqlDbType.Date, date)))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    intents[reader.GetGuid(0)] = (reader.GetInt32(1), reader.GetInt32(2), reader.GetDecimal(3), reader.GetBoolean(4));
                }
            }

            var ledger = await PairsAsync(connection, "SELECT account_id, count(*)::int, COALESCE(sum(notional), 0) FROM execution.ledger_entries WHERE session_date = @d GROUP BY account_id", cancellationToken, ("d", NpgsqlDbType.Date, date));
            var positions = await PairsAsync(connection, "SELECT account_id, count(*)::int, 0::numeric FROM execution.positions WHERE net_qty <> 0 GROUP BY account_id", cancellationToken);
            var tickets = await PairsAsync(
                connection,
                "SELECT t.account_id, count(*)::int, 0::numeric FROM execution.confirm_tickets t WHERE t.session_date = @d AND t.expires_at > @now AND NOT EXISTS (SELECT 1 FROM execution.ticket_outcomes o WHERE o.ticket_id = t.id) GROUP BY t.account_id",
                cancellationToken, ("d", NpgsqlDbType.Date, date), ("now", NpgsqlDbType.TimestampTz, now.UtcDateTime));
            var marks = await PairsAsync(connection, "SELECT account_id, 0, realised FROM execution.day_marks WHERE session_date = @d", cancellationToken, ("d", NpgsqlDbType.Date, date));
            var ids = intents.Keys.Concat(ledger.Keys).Concat(positions.Keys).Concat(tickets.Keys).Concat(marks.Keys).Distinct();
            return
            [
                .. ids.Select(id =>
                {
                    intents.TryGetValue(id, out var i);
                    ledger.TryGetValue(id, out var l);
                    positions.TryGetValue(id, out var p);
                    tickets.TryGetValue(id, out var t);
                    return new DayAccountRaw(id, l.Count, l.Amount, p.Count, t.Count, i.Intents, i.Filled, i.Risk, i.Inconsistent, marks.TryGetValue(id, out var m) ? m.Amount : null);
                }),
            ];
        }, cancellationToken);

    public Task<(bool? CanaryPending, bool? ExecutionEnabled)> SwitchesAsync(CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<(bool?, bool?)>(db, async connection =>
        {
            // No settings row at all is the default state: execution off and the canary pending (the registry default). A row without the key is the same default for the canary.
            await using var command = Command(connection, "SELECT execution_enabled, \"values\" ->> 'exec.canary_pending' FROM execution.settings WHERE effective_from <= now() ORDER BY id DESC LIMIT 1");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return (true, false);
            }

            return (reader.IsDBNull(1) ? true : bool.TryParse(reader.GetString(1), out var canary) ? canary : null, reader.GetBoolean(0));
        }, cancellationToken);

    public Task<IReadOnlyList<BlotterRaw>> BlotterAsync(DateOnly date, Guid? accountId, IReadOnlyList<string>? states, int skip, int take, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<BlotterRaw>>(db, async connection =>
        {
            var heads = new List<(long Id, Func<IReadOnlyList<BlotterFillRaw>, BlotterRaw> Make)>();
            await using (var command = Command(connection, """
                SELECT i.id, i.public_id, i.account_id, a.name, i.created_at, i.symbol, i.side, i.quantity, i.entry_price, s.state, s.filled_qty, e.client_order_id, e.broker_order_id, e.algo_id,
                       (SELECT l."by" FROM execution.action_log l WHERE l.intent_public_id = i.public_id AND l.kind IN ('auto_entry', 'tap_entry') ORDER BY l.id DESC LIMIT 1),
                       EXISTS (SELECT 1 FROM execution.mode_trail m WHERE m.id = i.mode_trail_id AND m.to_mode = 'assisted'),
                       (SELECT ev.payload ->> 'broker_code' FROM execution.events ev WHERE ev.intent_id = i.id AND ev.payload ->> 'broker_code' IS NOT NULL ORDER BY ev.id DESC LIMIT 1),
                       COALESCE((SELECT array_agg(cr.code ORDER BY cr.ord) FROM execution.check_results cr
                                 WHERE cr.run_id = (SELECT max(r.id) FROM execution.check_runs r WHERE r.intent_id = i.id AND r.verdict = 'rejected') AND cr.outcome IN ('fail', 'unavailable')), ARRAY[]::text[])
                FROM execution.intents i
                JOIN execution.intent_state s ON s.intent_id = i.id
                JOIN accounts.broker_accounts a ON a.id = i.account_id
                LEFT JOIN LATERAL (SELECT client_order_id, broker_order_id, algo_id FROM execution.legs WHERE intent_id = i.id AND kind = 'E' ORDER BY id LIMIT 1) e ON true
                WHERE i.session_date = @d AND (@acc IS NULL OR i.account_id = @acc) AND (@states IS NULL OR s.state = ANY(@states))
                ORDER BY i.id DESC OFFSET @skip LIMIT @take
                """, ("d", NpgsqlDbType.Date, date), ("acc", NpgsqlDbType.Uuid, accountId), ("states", NpgsqlDbType.Array | NpgsqlDbType.Varchar, states?.ToArray()), ("skip", NpgsqlDbType.Integer, skip), ("take", NpgsqlDbType.Integer, take)))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var id = reader.GetInt64(0);
                    var publicId = reader.GetGuid(1);
                    var account = reader.GetGuid(2);
                    var name = reader.GetString(3);
                    var at = reader.GetFieldValue<DateTimeOffset>(4);
                    var symbol = reader.GetString(5);
                    var side = reader.GetString(6);
                    var quantity = reader.GetInt32(7);
                    var price = reader.GetDecimal(8);
                    var state = reader.GetString(9);
                    var filled = reader.GetInt32(10);
                    var client = reader.IsDBNull(11) ? null : reader.GetString(11);
                    var broker = reader.IsDBNull(12) ? null : reader.GetString(12);
                    var algo = reader.IsDBNull(13) ? null : reader.GetString(13);
                    var by = reader.IsDBNull(14) ? null : reader.GetString(14);
                    var assisted = reader.GetBoolean(15);
                    var code = reader.IsDBNull(16) ? null : reader.GetString(16);
                    var rejections = reader.GetFieldValue<string[]>(17);
                    heads.Add((id, fills => new BlotterRaw(publicId, account, name, at, symbol, side, quantity, price, state, filled, client, broker, algo, by, assisted, code, rejections, fills)));
                }
            }

            var fillsByIntent = new Dictionary<long, List<BlotterFillRaw>>();
            if (heads.Count > 0)
            {
                await using var command = Command(
                    connection,
                    "SELECT f.intent_id, f.at, f.qty, f.price FROM execution.fills f JOIN execution.legs l ON l.id = f.leg_id WHERE f.intent_id = ANY(@ids) AND l.kind = 'E' ORDER BY f.at, f.id",
                    ("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint, heads.Select(h => h.Id).ToArray()));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var key = reader.GetInt64(0);
                    if (!fillsByIntent.TryGetValue(key, out var list))
                    {
                        fillsByIntent[key] = list = [];
                    }

                    list.Add(new BlotterFillRaw(reader.GetFieldValue<DateTimeOffset>(1), reader.GetInt32(2), reader.GetDecimal(3)));
                }
            }

            return [.. heads.Select(h => h.Make(fillsByIntent.TryGetValue(h.Id, out var f) ? f : []))];
        }, cancellationToken);

    public Task<IReadOnlyList<PositionRaw>> PositionsAsync(Guid? accountId, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<PositionRaw>>(db, async connection =>
        {
            await using var command = Command(connection, """
                SELECT p.account_id, a.name, p.symbol, p.net_qty, p.avg_price, oi.stop_price, oi.state,
                       (oi.id IS NOT NULL AND EXISTS (SELECT 1 FROM execution.alarms al WHERE al.intent_id = oi.id AND al.code = 'protection_breach' AND NOT EXISTS (SELECT 1 FROM execution.alarm_clears c WHERE c.alarm_id = al.id))),
                       oi.id IS NOT NULL
                FROM execution.positions p
                JOIN accounts.broker_accounts a ON a.id = p.account_id
                LEFT JOIN LATERAL (SELECT i.id, i.stop_price, s.state FROM execution.intents i JOIN execution.intent_state s ON s.intent_id = i.id
                                   WHERE i.account_id = p.account_id AND i.symbol = p.symbol AND s.state IN ('filled', 'protected', 'managing', 'closing') ORDER BY i.id DESC LIMIT 1) oi ON true
                WHERE p.net_qty <> 0 AND (@acc IS NULL OR p.account_id = @acc)
                ORDER BY a.name, p.symbol LIMIT 200
                """, ("acc", NpgsqlDbType.Uuid, accountId));
            var rows = new List<PositionRaw>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new PositionRaw(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetDecimal(4), reader.IsDBNull(5) ? null : reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetBoolean(7), reader.GetBoolean(8)));
            }

            return rows;
        }, cancellationToken);

    public Task<IReadOnlyList<ActivityRow>> ActivityAsync(DateOnly? date, string? kind, string? by, int skip, int take, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<ActivityRow>>(db, async connection =>
        {
            await using var command = Command(connection, """
                SELECT u.id, u.at, u.kind, u.who, u.account_id, u.text, u.codes FROM (
                    SELECT 'action:' || id AS id, at, kind::text AS kind, "by"::text AS who, account_id, text, codes::text AS codes, session_date AS day FROM execution.action_log
                    UNION ALL SELECT 'alarm:' || id, at, 'alarm', 'system', account_id, code || ': ' || detail, '[]', (at AT TIME ZONE 'Asia/Kolkata')::date FROM execution.alarms
                    UNION ALL SELECT 'halt:' || id, at, 'halt', CASE WHEN source = 'owner' THEN 'owner' ELSE 'system' END, account_id, 'Halt, level ' || level || ' (' || trigger_code || '): ' || reason, '[]', (at AT TIME ZONE 'Asia/Kolkata')::date FROM execution.halts
                    UNION ALL SELECT 'drill:' || id, at, 'drill', 'owner', account_id, CASE WHEN passed THEN 'Kill-switch drill passed in ' ELSE 'Kill-switch drill failed in ' END || latency_ms || ' ms: ' || detail, '[]', (at AT TIME ZONE 'Asia/Kolkata')::date FROM execution.drills
                    UNION ALL SELECT 'mode:' || id, changed_at, 'mode_change', CASE WHEN actor LIKE 'owner:%' THEN 'owner' ELSE 'system' END, account_id, 'Mode ' || from_mode || ' to ' || to_mode || ' (' || kind || '): ' || reason, '[]', (changed_at AT TIME ZONE 'Asia/Kolkata')::date FROM execution.mode_trail
                    UNION ALL SELECT 'settings:' || id, changed_at, 'settings_change', 'owner', NULL::uuid, 'Settings changed: ' || reason, '[]', (changed_at AT TIME ZONE 'Asia/Kolkata')::date FROM execution.settings
                ) u
                WHERE (@d IS NULL OR u.day = @d) AND (@kind IS NULL OR u.kind = @kind) AND (@by IS NULL OR u.who = @by)
                ORDER BY u.at DESC, u.id DESC OFFSET @skip LIMIT @take
                """, ("d", NpgsqlDbType.Date, date), ("kind", NpgsqlDbType.Text, kind), ("by", NpgsqlDbType.Text, by), ("skip", NpgsqlDbType.Integer, skip), ("take", NpgsqlDbType.Integer, take));
            var rows = new List<ActivityRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ActivityRow(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.GetString(5), Codes(reader.GetString(6))));
            }

            return rows;
        }, cancellationToken);

    public Task<NoticeRaw> NoticesAsync(DateTimeOffset since, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync(db, async connection =>
        {
            var tickets = new List<NoticeTicketRaw>();
            await using (var command = Command(
                connection,
                "SELECT t.id, t.account_id, t.text, t.issued_at, t.expires_at, o.outcome FROM execution.confirm_tickets t LEFT JOIN execution.ticket_outcomes o ON o.ticket_id = t.id WHERE t.expires_at >= @since ORDER BY t.issued_at DESC LIMIT 100",
                ("since", NpgsqlDbType.TimestampTz, since.UtcDateTime)))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    tickets.Add(new NoticeTicketRaw(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
                }
            }

            var actions = new List<NoticeActionRaw>();
            await using (var command = Command(connection, "SELECT id, account_id, kind, text, at FROM execution.action_log WHERE \"by\" = 'auto' AND at >= @since ORDER BY id DESC LIMIT 100", ("since", NpgsqlDbType.TimestampTz, since.UtcDateTime)))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    actions.Add(new NoticeActionRaw(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
                }
            }

            var rejected = new List<NoticeRejectedRaw>();
            await using (var command = Command(
                connection,
                "SELECT i.public_id, i.account_id, i.symbol, s.updated_at, i.session_date FROM execution.intents i JOIN execution.intent_state s ON s.intent_id = i.id WHERE s.state = 'rejected' AND s.updated_at >= @since ORDER BY s.updated_at DESC LIMIT 100",
                ("since", NpgsqlDbType.TimestampTz, since.UtcDateTime)))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rejected.Add(new NoticeRejectedRaw(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), DateOnly.FromDateTime(reader.GetDateTime(4))));
                }
            }

            return new NoticeRaw(tickets, actions, rejected);
        }, cancellationToken);

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, params (string Name, NpgsqlDbType Type, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, type, value) in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        }

        return command;
    }

    private static async Task<Dictionary<Guid, (int Count, decimal Amount)>> PairsAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, NpgsqlDbType Type, object? Value)[] parameters)
    {
        var result = new Dictionary<Guid, (int, decimal)>();
        await using var command = Command(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetGuid(0)] = (Convert.ToInt32(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture), reader.GetDecimal(2));
        }

        return result;
    }

    private static string[] Codes(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array ? [.. document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
