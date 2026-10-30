using Npgsql;
using NpgsqlTypes;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Readiness;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The readiness record as the role xd_exec (P12-11): insert-only checks and day states, a read of the owner's attestations (written by xd-api; xd_exec has SELECT only), the accounts at assisted (through the database's own <c>execution.effective_mode</c>) and the trading calendar
/// (a weekday is a session unless a confirmed holiday row says otherwise; a confirmed special row makes any date one).
/// </summary>
internal sealed class PgReadinessStore(NpgsqlDataSource data) : IReadinessStore, IReadinessAccounts, IConnectionFacts
{
    public async Task<ReadinessCheckRecord?> LastCheckAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT id, at, session_date, verdict, reasons::text FROM execution.readiness_checks WHERE account_id = @a ORDER BY id DESC LIMIT 1");
        command.Parameters.AddWithValue("a", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(1), accountId, reader.GetFieldValue<DateOnly>(2), ReadinessWords.State(reader.GetString(3)), ReadinessJson.Read(reader.GetString(4)))
            : null;
    }

    public async Task<ReadinessCheckRecord> AppendCheckAsync(ReadinessCheckDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var command = data.CreateCommand(
            "INSERT INTO execution.readiness_checks (at, account_id, session_date, verdict, reasons, token_expires_at, profile_read_at, ip_read_at, ip_modify_after) VALUES (@at, @a, @s, @v, @r, @t, @p, @i, @m) RETURNING id");
        command.Parameters.AddWithValue("at", draft.At);
        command.Parameters.AddWithValue("a", draft.AccountId);
        command.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Date) { Value = draft.SessionDate });
        command.Parameters.AddWithValue("v", ReadinessWords.Word(draft.Verdict));
        command.Parameters.Add(new NpgsqlParameter("r", NpgsqlDbType.Jsonb) { Value = ReadinessJson.Write(draft.Reasons) });
        command.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.TimestampTz) { Value = (object?)draft.TokenExpiresAt ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("p", NpgsqlDbType.TimestampTz) { Value = (object?)draft.ProfileReadAt ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("i", NpgsqlDbType.TimestampTz) { Value = (object?)draft.IpReadAt ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("m", NpgsqlDbType.Date) { Value = (object?)draft.IpModifyAfter ?? DBNull.Value });
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        return new(id, draft.At, draft.AccountId, draft.SessionDate, draft.Verdict, draft.Reasons);
    }

    public async Task<bool> HasDayStateAsync(Guid accountId, DateOnly session, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT EXISTS (SELECT 1 FROM execution.day_states WHERE account_id = @a AND session_date = @s)");
        command.Parameters.AddWithValue("a", accountId);
        command.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Date) { Value = session });
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<bool> AppendDayStateAsync(DayStateDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var command = data.CreateCommand(
            "INSERT INTO execution.day_states (account_id, session_date, state, reasons, check_id, late, at) VALUES (@a, @s, @st, @r, @c, @l, @at) ON CONFLICT (account_id, session_date) DO NOTHING");
        command.Parameters.AddWithValue("a", draft.AccountId);
        command.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Date) { Value = draft.SessionDate });
        command.Parameters.AddWithValue("st", ReadinessWords.Word(draft.State));
        command.Parameters.Add(new NpgsqlParameter("r", NpgsqlDbType.Jsonb) { Value = ReadinessJson.Write(draft.Reasons) });
        command.Parameters.Add(new NpgsqlParameter("c", NpgsqlDbType.Bigint) { Value = (object?)draft.CheckId ?? DBNull.Value });
        command.Parameters.AddWithValue("l", draft.Late);
        command.Parameters.AddWithValue("at", draft.At);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<IpAttestation?> NewestAttestationAsync(Guid accountId, string ip, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT ip, orders_allowed, at FROM execution.ip_attestations WHERE account_id = @a AND ip = @ip ORDER BY id DESC LIMIT 1");
        command.Parameters.AddWithValue("a", accountId);
        command.Parameters.AddWithValue("ip", ip);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? new(accountId, reader.GetString(0), reader.GetBoolean(1), reader.GetFieldValue<DateTimeOffset>(2)) : null;
    }

    public async Task<IReadOnlyList<Guid>> AssistedAccountsAsync(CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT a.id FROM accounts.broker_accounts a WHERE execution.effective_mode(a.id) = 'assisted' ORDER BY a.id");
        List<Guid> ids = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    public async Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = data.CreateCommand("SELECT kind FROM market.trading_days WHERE date = @d AND status = 'confirmed'");
        command.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date) { Value = date });
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) switch
        {
            "special" => true,
            "holiday" => false,
            _ => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        };
    }

    public async Task<ConnectionFacts?> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = data.CreateCommand("SELECT status, expires_at FROM accounts.broker_connections WHERE account_id = @id");
            command.Parameters.AddWithValue("id", accountId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? new ConnectionFacts(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1)) : null;
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or InvalidCastException)
        {
            return null;
        }
    }
}
