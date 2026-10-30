using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The entry ledger and the action log as the role xd_exec (spec P12-09, insert-only). A reservation writes the action-log row and the ledger row in ONE transaction; the trigger <c>execution.check_ledger_entry</c> takes an advisory lock per
/// account and refuses a row that breaks the sheet in force, whose action-log row is not of the same transaction, or whose account is not in <c>semi_auto</c> or <c>auto_live</c>; its code comes back as the refusal and nothing is left.
/// </summary>
internal sealed class PgLedger(NpgsqlDataSource data) : ILedger, IActionLog
{
    private const string Prefix = "execution.ledger_refused: ";

    private const string InsertAction = """
        INSERT INTO execution.action_log (account_id, session_date, kind, "by", intent_public_id, ticket_id, text, codes, settings_id, limits_id)
        VALUES (@account, (clock_timestamp() AT TIME ZONE 'Asia/Kolkata')::date, @kind, @by, @intent, @ticket, @text, @codes, @settings, @limits)
        RETURNING id
        """;

    private const string InsertLedger = """
        INSERT INTO execution.ledger_entries (account_id, session_date, intent_public_id, symbol, strategy_hash, product, side, quantity, notional, risk_at_stop, mode, limits_id, settings_id, action_log_id)
        VALUES (@account, @session, @intent, @symbol, @strategy, @product, @side, @quantity, @notional, @risk, @mode, @limits, @settings, @action)
        RETURNING id
        """;

    public async Task<LedgerDay?> DayAsync(Guid accountId, DateOnly session, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = data.CreateCommand("SELECT count(*)::int, COALESCE(sum(notional), 0) FROM execution.ledger_entries WHERE account_id = @a AND session_date = @d");
            command.Parameters.AddWithValue("a", accountId);
            command.Parameters.AddWithValue("d", session);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new LedgerDay(reader.GetInt32(0), reader.GetDecimal(1));
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    public async Task<bool?> StrategyUsedAsync(Guid accountId, string strategyHash, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = data.CreateCommand("SELECT EXISTS (SELECT 1 FROM execution.ledger_entries WHERE account_id = @a AND strategy_hash = @h)");
            command.Parameters.AddWithValue("a", accountId);
            command.Parameters.AddWithValue("h", strategyHash);
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    public async Task<LedgerResult> ReserveAsync(LedgerReservation entry, ActionLogEntry action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(action);
        await using var connection = await data.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long actionId;
            await using (var insert = new NpgsqlCommand(InsertAction, connection, transaction))
            {
                Bind(insert, action);
                actionId = (long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }

            long ledgerId;
            await using (var insert = new NpgsqlCommand(InsertLedger, connection, transaction))
            {
                insert.Parameters.AddWithValue("account", entry.AccountId);
                insert.Parameters.AddWithValue("session", entry.Session);
                insert.Parameters.AddWithValue("intent", entry.IntentPublicId);
                insert.Parameters.AddWithValue("symbol", entry.Symbol);
                insert.Parameters.AddWithValue("strategy", entry.StrategyHash);
                insert.Parameters.AddWithValue("product", entry.Product);
                insert.Parameters.AddWithValue("side", entry.Side);
                insert.Parameters.AddWithValue("quantity", entry.Quantity);
                insert.Parameters.AddWithValue("notional", entry.Notional);
                insert.Parameters.AddWithValue("risk", entry.RiskAtStop);
                insert.Parameters.AddWithValue("mode", entry.Mode);
                insert.Parameters.AddWithValue("limits", entry.LimitsId);
                insert.Parameters.AddWithValue("settings", entry.SettingsId);
                insert.Parameters.AddWithValue("action", actionId);
                ledgerId = (long)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new LedgerResult(ledgerId, actionId, null);
        }
        catch (PostgresException failure) when (failure.MessageText.StartsWith(Prefix, StringComparison.Ordinal) || failure.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        {
            if (transaction.Connection is not null) // a refusal at commit (the deferred check) has already ended the transaction
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return new LedgerResult(null, null, Code(failure));
        }
        catch (NpgsqlException)
        {
            return new LedgerResult(null, null, AutoLiveCodes.LedgerUnavailable); // nothing was committed; disposing the transaction rolls it back
        }
    }

    public async Task<long?> AppendAsync(ActionLogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            await using var insert = data.CreateCommand(InsertAction);
            Bind(insert, entry);
            return (long?)await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    /// <summary>The rule a refusal names: the code after the prefix (up to the next colon), or a generic one for a constraint.</summary>
    internal static string Code(PostgresException failure)
    {
        if (failure.MessageText.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var rest = failure.MessageText[Prefix.Length..];
            var colon = rest.IndexOf(':', StringComparison.Ordinal);
            return colon > 0 ? rest[..colon] : rest;
        }

        return failure.SqlState == PostgresErrorCodes.UniqueViolation ? "ledger.duplicate" : "ledger.refused";
    }

    private static void Bind(NpgsqlCommand command, ActionLogEntry entry)
    {
        command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Uuid) { Value = (object?)entry.AccountId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Varchar) { Value = entry.Kind });
        command.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Varchar) { Value = entry.By });
        command.Parameters.Add(new NpgsqlParameter("intent", NpgsqlDbType.Uuid) { Value = (object?)entry.IntentPublicId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("ticket", NpgsqlDbType.Uuid) { Value = (object?)entry.TicketId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("text", NpgsqlDbType.Text) { Value = entry.Text });
        command.Parameters.Add(new NpgsqlParameter("codes", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(entry.Codes) });
        command.Parameters.Add(new NpgsqlParameter("settings", NpgsqlDbType.Bigint) { Value = (object?)entry.SettingsId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("limits", NpgsqlDbType.Bigint) { Value = (object?)entry.LimitsId ?? DBNull.Value });
    }
}
