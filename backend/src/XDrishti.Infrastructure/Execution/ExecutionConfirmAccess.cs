using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Execution;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>Maps a refusal raised by a trigger of <c>AddConfirmTickets</c> (<c>execution.&lt;code&gt;: text</c>) to its code.</summary>
internal static class ConfirmRefusal
{
    public static string Code(PostgresException failure) =>
        failure.MessageText.StartsWith("execution.", StringComparison.Ordinal) && failure.MessageText.IndexOf(':', StringComparison.Ordinal) is > 0 and var colon
            ? failure.MessageText[..colon]
            : ConfirmCodes.ValuesInvalid;
}

/// <summary>
/// The confirm tickets as the runtime role (spec P12-08b, ADR 0166): SELECT on the tickets and outcomes, and INSERT of the owner's <c>confirmed</c> and <c>skipped</c> outcomes. The trigger <c>execution.check_ticket_outcome</c> judges the
/// tap again by the database clock; its refusal comes back as the code. The facts of the tap are one read.
/// </summary>
internal sealed class ConfirmTicketAccess(XDrishtiDbContext db) : IConfirmTicketAccess
{
    private const string Select = """
        SELECT t.id, t.account_id, COALESCE(a.name, ''), t.intent_public_id, t.plan_item_id, t.session_date, t.kind, t.text, t.text_hash, t.client_order_id, t.ref_price, t.notional, t.risk_at_stop, t.requires_password,
               t.password_reasons::text, t.soft_codes, t.issued_at, t.expires_at, o.outcome, o.reason, o.decided_at
          FROM execution.confirm_tickets t
          LEFT JOIN accounts.broker_accounts a ON a.id = t.account_id
          LEFT JOIN execution.ticket_outcomes o ON o.ticket_id = t.id
        """;

    private const string Facts = """
        SELECT clock_timestamp(),
               COALESCE((SELECT s.execution_enabled FROM execution.settings s ORDER BY s.id DESC LIMIT 1), false),
               execution.effective_mode(@a),
               EXISTS (SELECT 1 FROM execution.halts h WHERE (h.scope = 'system' OR h.account_id = @a)
                       AND NOT EXISTS (SELECT 1 FROM execution.halt_releases r WHERE r.halt_id = h.id) AND NOT EXISTS (SELECT 1 FROM execution.halt_auto_clears c WHERE c.halt_id = h.id)),
               rc.verdict, rc.session_date, rc.at,
               EXISTS (SELECT 1 FROM execution.day_unlocks u WHERE u.account_id = @a AND u.session_date = @d AND u.expires_at > clock_timestamp()),
               q.price, q.at,
               (SELECT ARRAY(SELECT x.code::text FROM execution.check_results x WHERE x.run_id = r.id AND x.class = 'soft' AND x.outcome <> 'pass' ORDER BY x.code)
                  FROM execution.check_runs r JOIN execution.intents i ON i.id = r.intent_id WHERE i.public_id = @intent AND r.kind = 'full' ORDER BY r.id DESC LIMIT 1),
               (SELECT s."values"::text FROM execution.settings s ORDER BY s.id DESC LIMIT 1)
          FROM (SELECT 1) one
          LEFT JOIN LATERAL (SELECT c.verdict, c.session_date, c.at FROM execution.readiness_checks c WHERE c.account_id = @a AND c.session_date = @d ORDER BY c.id DESC LIMIT 1) rc ON true
          LEFT JOIN LATERAL (SELECT CASE WHEN count(*) = 1 THEN max(l.last_price) END AS price, CASE WHEN count(*) = 1 THEN max(l.tick_at) END AS at
                               FROM market.live_quotes l JOIN market.instruments m ON m.id = l.instrument_id JOIN execution.intents i ON i.symbol = m.canonical_symbol
                              WHERE i.public_id = @intent AND m.retired_at IS NULL) q ON true
        """;

    public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => DatabaseClock.NowAsync(db, cancellationToken);

    public Task<IReadOnlyList<ConfirmTicketRow>> ListAsync(DateOnly session, bool openOnly, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<ConfirmTicketRow>>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(Select + " WHERE t.session_date = @d AND (@open = false OR o.ticket_id IS NULL) ORDER BY t.issued_at DESC, t.id LIMIT 200", connection);
            command.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date) { Value = session });
            command.Parameters.Add(new NpgsqlParameter("open", NpgsqlDbType.Boolean) { Value = openOnly });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<ConfirmTicketRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(Read(reader));
            }

            return rows;
        }, cancellationToken);

    public Task<ConfirmTicketRow?> FindAsync(Guid ticketId, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<ConfirmTicketRow?>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(Select + " WHERE t.id = @id", connection);
            command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = ticketId });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }, cancellationToken);

    public Task<ConfirmFacts> FactsAsync(ConfirmTicketRow ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        // A database that cannot be read throws (500): an unknown failure is never turned into a refusal or a pass.
        return ExecutionConnection.UseAsync(db, async connection =>
        {
            await using var command = new NpgsqlCommand(Facts, connection);
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = ticket.AccountId });
            command.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date) { Value = ticket.SessionDate });
            command.Parameters.Add(new NpgsqlParameter("intent", NpgsqlDbType.Uuid) { Value = ticket.IntentPublicId });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var state = new ConfirmState(
                reader.GetFieldValue<DateTimeOffset>(0), ticket.ExpiresAt, ticket.SessionDate, reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6), reader.GetBoolean(7));
            return new ConfirmFacts(
                state, reader.IsDBNull(8) ? null : reader.GetDecimal(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9), reader.IsDBNull(10) ? null : reader.GetFieldValue<string[]>(10),
                ConfirmSettings.FromValues(reader.IsDBNull(11) ? null : Values(reader.GetString(11))));
        }, cancellationToken);
    }

    public Task<OutcomeWrite> InsertOutcomeAsync(Guid ticketId, string outcome, string reason, string actor, string? note, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync(db, async connection =>
        {
            await using var command = new NpgsqlCommand("INSERT INTO execution.ticket_outcomes (ticket_id, outcome, reason, actor, note) VALUES (@t, @o, @r, @a, @n) RETURNING decided_at", connection);
            command.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Uuid) { Value = ticketId });
            command.Parameters.Add(new NpgsqlParameter("o", NpgsqlDbType.Varchar) { Value = outcome });
            command.Parameters.Add(new NpgsqlParameter("r", NpgsqlDbType.Varchar) { Value = reason });
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Varchar) { Value = actor });
            command.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Varchar) { Value = (object?)note ?? DBNull.Value });
            try
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                return new OutcomeWrite(reader.GetFieldValue<DateTimeOffset>(0), false, null);
            }
            catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return new OutcomeWrite(null, true, null);
            }
            catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation)
            {
                return new OutcomeWrite(null, false, ConfirmRefusal.Code(failure));
            }
        }, cancellationToken);

    private static Dictionary<string, string> Values(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind != JsonValueKind.Object
            ? []
            : document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText(), StringComparer.Ordinal);
    }

    private static List<string> Strings(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array ? [.. document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)] : [];
    }

    private static ConfirmTicketRow Read(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetGuid(3), r.GetInt64(4), r.GetFieldValue<DateOnly>(5), r.GetString(6), r.GetString(7), r.GetString(8).Trim(), r.GetString(9), r.GetDecimal(10), r.GetDecimal(11), r.GetDecimal(12),
        r.GetBoolean(13), Strings(r.GetString(14)), r.GetFieldValue<string[]>(15), r.GetFieldValue<DateTimeOffset>(16), r.GetFieldValue<DateTimeOffset>(17), r.IsDBNull(18) ? null : r.GetString(18), r.IsDBNull(19) ? null : r.GetString(19),
        r.IsDBNull(20) ? null : r.GetFieldValue<DateTimeOffset>(20));
}

/// <summary>The day unlocks as the runtime role: SELECT, and INSERT of the owner's unlock; the trigger stamps the time and the 15:30 India expiry and refuses another day or a late unlock.</summary>
internal sealed class DayUnlockAccess(XDrishtiDbContext db) : IDayUnlockAccess
{
    private const string Select = "SELECT id, account_id, session_date, unlocked_at, expires_at FROM execution.day_unlocks";

    public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => DatabaseClock.NowAsync(db, cancellationToken);

    public Task<IReadOnlyList<DayUnlockView>> ListAsync(DateOnly session, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<DayUnlockView>>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(Select + " WHERE session_date = @d ORDER BY id", connection);
            command.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date) { Value = session });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<DayUnlockView>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(Read(reader));
            }

            return rows;
        }, cancellationToken);

    public Task<string?> EffectiveModeAsync(Guid accountId, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync(db, async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT execution.effective_mode(a.id) FROM accounts.broker_accounts a WHERE a.id = @a AND a.archived_at IS NULL", connection);
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId });
            return await command.ExecuteScalarAsync(cancellationToken) as string;
        }, cancellationToken);

    public Task<(DayUnlockView? Row, string? Refused)> InsertAsync(Guid accountId, DateOnly session, string actor, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<(DayUnlockView?, string?)>(db, async connection =>
        {
            await using (var command = new NpgsqlCommand(
                "INSERT INTO execution.day_unlocks (account_id, session_date, expires_at, actor) VALUES (@a, @d, @e, @by) ON CONFLICT (account_id, session_date) DO NOTHING", connection))
            {
                command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId });
                command.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date) { Value = session });
                command.Parameters.Add(new NpgsqlParameter("e", NpgsqlDbType.TimestampTz) { Value = DayUnlock.ExpiresAt(session).ToUniversalTime() });
                command.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Varchar) { Value = actor });
                try
                {
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.CheckViolation)
                {
                    return (null, ConfirmRefusal.Code(failure));
                }
            }

            await using var read = new NpgsqlCommand(Select + " WHERE account_id = @a AND session_date = @d", connection);
            read.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId });
            read.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date) { Value = session });
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? (Read(reader), null) : (null, ConfirmCodes.UnlockClosed);
        }, cancellationToken);

    private static DayUnlockView Read(NpgsqlDataReader r) => new(r.GetInt64(0), r.GetGuid(1), r.GetFieldValue<DateOnly>(2), r.GetFieldValue<DateTimeOffset>(3), r.GetFieldValue<DateTimeOffset>(4));
}

/// <summary>The database clock: the confirm path is judged by it, never by the browser's or this process's clock.</summary>
internal static class DatabaseClock
{
    public static Task<DateTimeOffset> NowAsync(XDrishtiDbContext db, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync(db, async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT clock_timestamp()", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return reader.GetFieldValue<DateTimeOffset>(0);
        }, cancellationToken);
}
