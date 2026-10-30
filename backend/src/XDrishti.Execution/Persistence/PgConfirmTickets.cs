using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The confirm record as the role xd_exec (P12-08b): it inserts tickets and the system's two outcomes (<c>expired</c>, <c>refused</c>) and reads the intent, the plan approval, the account's name and fixed capital and the newest full check run.
/// The triggers of <c>AddConfirmTickets</c> judge every row again (issued now, one BUY intent in checking, a second attempt only after a refused first; an expiry only at or after the expiry). Nothing is updated or deleted.
/// </summary>
internal sealed class PgConfirmTickets(NpgsqlDataSource data) : IConfirmTicketStore, IConfirmDecisions
{
    private const string TicketSelect = """
        SELECT t.id, t.intent_public_id, t.account_id, t.attempt, t.text, t.text_hash, t.requires_password, t.issued_at, t.expires_at, o.outcome, o.reason, o.decided_at
          FROM execution.confirm_tickets t LEFT JOIN execution.ticket_outcomes o ON o.ticket_id = t.id
        """;

    private const string Context = """
        SELECT a.broker, a.name,
               (SELECT ap.decided_at FROM workflow.approvals ap WHERE ap.id = i.approval_id),
               (SELECT CASE WHEN s.capital_source = 'Fixed' THEN s.capital_amount END FROM accounts.trading_settings s WHERE s.account_id = i.account_id),
               r.id, r.hard, r.soft
          FROM execution.intents i
          JOIN accounts.broker_accounts a ON a.id = i.account_id
          LEFT JOIN LATERAL (
                SELECT c.id,
                       (SELECT count(*) FROM execution.check_results x WHERE x.run_id = c.id AND x.class = 'hard' AND x.outcome <> 'pass') AS hard,
                       ARRAY(SELECT x.code::text FROM execution.check_results x WHERE x.run_id = c.id AND x.class = 'soft' AND x.outcome <> 'pass' ORDER BY x.code) AS soft
                  FROM execution.check_runs c WHERE c.intent_id = i.id AND c.kind = 'full' ORDER BY c.id DESC LIMIT 1) r ON true
         WHERE i.id = @id
        """;

    private const string Insert = """
        INSERT INTO execution.confirm_tickets (intent_public_id, account_id, plan_item_id, session_date, attempt, kind, params, text, text_hash, client_order_id, ref_price, notional, risk_at_stop, requires_password, password_reasons,
                                              soft_codes, check_run_id, issued_at, expires_at)
        VALUES (@intent, @account, @item, @session, @attempt, 'entry', @params, @text, @hash, @coid, @ref, @notional, @risk, @password, @reasons, @soft, @run, @issued, @expires)
        RETURNING id
        """;

    private const string Expire = """
        WITH due AS (
            INSERT INTO execution.ticket_outcomes (ticket_id, outcome, reason, actor)
            SELECT t.id, 'expired', 'ttl', @actor FROM execution.confirm_tickets t
             WHERE t.expires_at <= clock_timestamp() AND NOT EXISTS (SELECT 1 FROM execution.ticket_outcomes o WHERE o.ticket_id = t.id)
            ON CONFLICT (ticket_id) DO NOTHING
            RETURNING ticket_id, decided_at)
        SELECT d.ticket_id, t.intent_public_id, t.account_id, (extract(epoch FROM d.decided_at - t.issued_at) * 1000)::bigint FROM due d JOIN execution.confirm_tickets t ON t.id = d.ticket_id
        """;

    public async Task<TicketIntent?> IntentAsync(long intentId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(
            "SELECT i.id, i.public_id, i.account_id, i.plan_item_id, i.session_date, i.symbol, i.side, i.quantity, i.entry_price, i.stop_price, i.target_price, s.state FROM execution.intents i JOIN execution.intent_state s ON s.intent_id = i.id WHERE i.id = @id");
        command.Parameters.AddWithValue("id", intentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new TicketIntent(
                reader.GetInt64(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt64(3), reader.GetFieldValue<DateOnly>(4), reader.GetString(5), reader.GetString(6) == "buy" ? ExecSide.Buy : ExecSide.Sell, reader.GetInt32(7),
                reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10), Enum.Parse<ExecState>(reader.GetString(11), ignoreCase: true))
            : null;
    }

    public async Task<TicketContext?> ContextAsync(TicketIntent intent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        try
        {
            await using var command = data.CreateCommand(Context);
            command.Parameters.AddWithValue("id", intent.IntentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var run = reader.IsDBNull(4) ? null : new TicketRun(reader.GetInt64(4), (int)reader.GetInt64(5), reader.GetFieldValue<string[]>(6));
            return new TicketContext(
                reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2), reader.IsDBNull(3) ? null : reader.GetDecimal(3), run);
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<StoredTicket>> TicketsAsync(Guid intentPublicId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(TicketSelect + " WHERE t.intent_public_id = @p ORDER BY t.attempt");
        command.Parameters.AddWithValue("p", intentPublicId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<StoredTicket>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public async Task<int> OpenCountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(
            "SELECT count(*) FROM execution.confirm_tickets t WHERE t.account_id = @a AND t.expires_at > clock_timestamp() AND NOT EXISTS (SELECT 1 FROM execution.ticket_outcomes o WHERE o.ticket_id = t.id)");
        command.Parameters.AddWithValue("a", accountId);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<TicketInsert> InsertAsync(TicketDraft draft, DateTimeOffset issuedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var p = draft.Params;
        await using var connection = await data.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Guid id;
            await using (var insert = new NpgsqlCommand(Insert, connection, tx))
            {
                insert.Parameters.AddWithValue("intent", draft.Intent.PublicId);
                insert.Parameters.AddWithValue("account", draft.Intent.AccountId);
                insert.Parameters.AddWithValue("item", draft.Intent.PlanItemId);
                insert.Parameters.Add(new NpgsqlParameter("session", NpgsqlDbType.Date) { Value = draft.Intent.Session });
                insert.Parameters.Add(new NpgsqlParameter("attempt", NpgsqlDbType.Smallint) { Value = (short)draft.Attempt });
                insert.Parameters.Add(new NpgsqlParameter("params", NpgsqlDbType.Jsonb) { Value = ParamsJson(p) });
                insert.Parameters.AddWithValue("text", draft.Text);
                insert.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Char) { Value = draft.TextHash });
                insert.Parameters.AddWithValue("coid", draft.ClientOrderId);
                insert.Parameters.AddWithValue("ref", draft.RefPrice);
                insert.Parameters.AddWithValue("notional", p.Notional);
                insert.Parameters.AddWithValue("risk", p.RiskAtStop);
                insert.Parameters.AddWithValue("password", draft.Password.Required);
                insert.Parameters.Add(new NpgsqlParameter("reasons", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(draft.Password.Reasons) });
                insert.Parameters.Add(new NpgsqlParameter("soft", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = draft.SoftCodes.ToArray() });
                insert.Parameters.Add(new NpgsqlParameter("run", NpgsqlDbType.Bigint) { Value = (object?)draft.CheckRunId ?? DBNull.Value });
                insert.Parameters.AddWithValue("issued", issuedAt);
                insert.Parameters.AddWithValue("expires", expiresAt);
                id = (Guid)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }

            if (draft.RefuseAs is { } reason)
            {
                await using var refuse = new NpgsqlCommand("INSERT INTO execution.ticket_outcomes (ticket_id, outcome, reason, actor) VALUES (@t, 'refused', @r, @a)", connection, tx);
                refuse.Parameters.AddWithValue("t", id);
                refuse.Parameters.AddWithValue("r", reason);
                refuse.Parameters.AddWithValue("a", TicketOutcomes.TicketActor);
                await refuse.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            StoredTicket stored;
            await using (var read = new NpgsqlCommand(TicketSelect + " WHERE t.id = @id", connection, tx))
            {
                read.Parameters.AddWithValue("id", id);
                await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                stored = Read(reader);
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new TicketInsert(stored, null);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new TicketInsert(null, null);
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            return new TicketInsert(null, e.MessageText);
        }
    }

    public async Task<IReadOnlyList<ClosedTicket>> ExpireDueAsync(string actor, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(Expire);
        command.Parameters.AddWithValue("actor", actor);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<ClosedTicket>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new ClosedTicket(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), TicketOutcomes.Expired, TicketOutcomes.TtlReason, reader.GetInt64(3)));
        }

        return list;
    }

    public async Task<bool?> RefuseAsync(Guid ticketId, string reason, string actor, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("INSERT INTO execution.ticket_outcomes (ticket_id, outcome, reason, actor) SELECT @t, 'refused', @r, @a WHERE EXISTS (SELECT 1 FROM execution.confirm_tickets WHERE id = @t) ON CONFLICT (ticket_id) DO NOTHING RETURNING 1");
        command.Parameters.AddWithValue("t", ticketId);
        command.Parameters.AddWithValue("r", reason);
        command.Parameters.AddWithValue("a", actor);
        if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return true;
        }

        await using var exists = data.CreateCommand("SELECT EXISTS (SELECT 1 FROM execution.confirm_tickets WHERE id = @t)");
        exists.Parameters.AddWithValue("t", ticketId);
        return (bool)(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! ? false : null;
    }

    public async Task<ConfirmDecision?> NewestAsync(long intentId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(
            "SELECT t.id, o.outcome, o.decided_at, clock_timestamp() FROM execution.intents i JOIN execution.confirm_tickets t ON t.intent_public_id = i.public_id LEFT JOIN execution.ticket_outcomes o ON o.ticket_id = t.id WHERE i.id = @id ORDER BY t.attempt DESC LIMIT 1");
        command.Parameters.AddWithValue("id", intentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new ConfirmDecision(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3))
            : null;
    }

    private static StoredTicket Read(NpgsqlDataReader r) => new(
        r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetInt16(3), r.GetString(4), r.GetString(5).Trim(), r.GetBoolean(6), r.GetFieldValue<DateTimeOffset>(7), r.GetFieldValue<DateTimeOffset>(8),
        r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetFieldValue<DateTimeOffset>(11));

    private static string ParamsJson(ConfirmTicketParams p) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["side"] = p.Side,
        ["quantity"] = p.Quantity,
        ["symbol"] = p.Symbol,
        ["limit"] = p.Limit,
        ["stop"] = p.Stop,
        ["target"] = p.Target,
        ["broker"] = p.Broker,
        ["accountName"] = p.AccountName,
        ["approvedAt"] = p.ApprovedAt,
        ["expiresAt"] = p.ExpiresAt,
        ["canary"] = p.Canary,
    });
}
