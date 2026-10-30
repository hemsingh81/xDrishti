using Npgsql;
using NpgsqlTypes;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The intents of the intent creator as the role xd_exec (P12-08a). An insert names the plan, the ticket, the approval and the mode trail row in force (the newest row of the account that has taken effect; none while the account has no
/// trail); the unique key (account, ticket) turns a second insert into nothing, and the start trigger of P12-01b gives the new intent its planned state row. Nothing is updated or deleted; moves are events.
/// </summary>
internal sealed class PgIntentRecord(NpgsqlDataSource data) : IIntentRecord
{
    private const string Insert = """
        INSERT INTO execution.intents (account_id, plan_id, plan_item_id, approval_id, mode_trail_id, session_date, symbol, security_id, side, quantity, entry_price, stop_price, target_price, structure, environment, created_by)
        VALUES (@a, @plan, @item, @approval,
                (SELECT t.id FROM execution.mode_trail t WHERE t.account_id = @a AND t.effective_from <= now() ORDER BY t.id DESC LIMIT 1),
                @s, @symbol, @security, 'buy', @q, @entry, @stop, @target, 'super', @env, @by)
        ON CONFLICT (account_id, plan_item_id) DO NOTHING
        RETURNING id, public_id, mode_trail_id
        """;

    private const string Since = """
        SELECT i.id, i.public_id, i.account_id, i.plan_item_id, i.session_date, i.symbol, i.side, i.entry_price, s.state, s.seq, i.mode_trail_id
          FROM execution.intents i JOIN execution.intent_state s ON s.intent_id = i.id
         WHERE i.session_date >= @s AND i.created_by = @by
         ORDER BY i.id
        """;

    public async Task<IntentRow?> CreateAsync(IntentDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Environment == ExecEnvironment.None)
        {
            return null;
        }

        await using var command = data.CreateCommand(Insert);
        command.Parameters.AddWithValue("a", draft.AccountId);
        command.Parameters.AddWithValue("plan", draft.PlanId);
        command.Parameters.AddWithValue("item", draft.PlanItemId);
        command.Parameters.Add(new NpgsqlParameter("approval", NpgsqlDbType.Bigint) { Value = (object?)draft.ApprovalId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Date) { Value = draft.Session });
        command.Parameters.AddWithValue("symbol", draft.Symbol);
        command.Parameters.AddWithValue("security", draft.SecurityId);
        command.Parameters.AddWithValue("q", draft.Quantity);
        command.Parameters.AddWithValue("entry", draft.Entry);
        command.Parameters.AddWithValue("stop", draft.Stop);
        command.Parameters.AddWithValue("target", draft.Target);
        command.Parameters.AddWithValue("env", draft.Environment == ExecEnvironment.Live ? "live" : "sandbox");
        command.Parameters.AddWithValue("by", draft.CreatedBy);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return new IntentRow(
                reader.GetInt64(0), reader.GetGuid(1), draft.AccountId, draft.PlanItemId, draft.Session, draft.Symbol, ExecSide.Buy, draft.Entry, ExecState.Planned, 0, reader.IsDBNull(2) ? null : reader.GetInt64(2));
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.StringDataRightTruncation or PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<IntentRow>> SinceAsync(DateOnly fromSession, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand(Since);
        command.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Date) { Value = fromSession });
        command.Parameters.AddWithValue("by", Application.Intents.IntentCreator.Actor);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<IntentRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new(
                reader.GetInt64(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt64(3), reader.GetFieldValue<DateOnly>(4), reader.GetString(5), reader.GetString(6) == "buy" ? ExecSide.Buy : ExecSide.Sell, reader.GetDecimal(7),
                Enum.Parse<ExecState>(reader.GetString(8), ignoreCase: true), reader.GetInt32(9), reader.IsDBNull(10) ? null : reader.GetInt64(10)));
        }

        return list;
    }
}
