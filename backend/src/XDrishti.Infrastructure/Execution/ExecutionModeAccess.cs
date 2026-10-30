using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Execution;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Execution;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>
/// The mode record of the execution schema as the runtime role (spec P12-07, ADR 0163): SELECT on <c>mode_trail</c> and <c>promotion_requests</c>, and INSERT into both, which are the owner acts. The triggers judge the order, the window and the shape;
/// their message comes back as the refusal.
/// </summary>
internal sealed class ExecutionModeStore(XDrishtiDbContext db) : IExecutionModeStore
{
    private const string Prefix = "execution.mode_refused: ";

    private const string TrailSelect = "SELECT id, account_id, from_mode, to_mode, kind, actor, reason, request_id, effective_from, changed_at FROM execution.mode_trail";

    private const string RequestSelect = "SELECT id, account_id, from_mode, to_mode, requested_at, eligible_from, expires_at, evidence_hash FROM execution.promotion_requests";

    public Task<IReadOnlyList<ModeAccountRow>> AccountsAsync(CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<ModeAccountRow>>(db, async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT a.id, a.name, t.mode FROM accounts.broker_accounts a JOIN accounts.trading_settings t ON t.account_id = a.id WHERE a.archived_at IS NULL AND t.enabled ORDER BY a.name", connection);
            var rows = new List<ModeAccountRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ModeAccountRow(reader.GetGuid(0), reader.GetString(1), Enum.TryParse<TradingMode>(reader.GetString(2), out var mode) ? mode : TradingMode.Paper));
            }

            return rows;
        }, cancellationToken);

    public Task<IReadOnlyList<ModeTrailRow>> TrailAsync(Guid accountId, int take, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<ModeTrailRow>>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(TrailSelect + " WHERE account_id = @a ORDER BY id DESC LIMIT @n", connection);
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId });
            command.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Integer) { Value = take });
            var rows = new List<ModeTrailRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ModeTrailRow(reader.GetInt64(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.GetFieldValue<DateTimeOffset>(8), reader.GetFieldValue<DateTimeOffset>(9)));
            }

            return rows;
        }, cancellationToken);

    public Task<ModeRequestRow?> NewestRequestAsync(Guid accountId, CancellationToken cancellationToken) =>
        RequestAsync(RequestSelect + " WHERE account_id = @k ORDER BY requested_at DESC, id DESC LIMIT 1", NpgsqlDbType.Uuid, accountId, cancellationToken);

    public Task<ModeRequestRow?> FindRequestAsync(Guid id, CancellationToken cancellationToken) =>
        RequestAsync(RequestSelect + " WHERE id = @k", NpgsqlDbType.Uuid, id, cancellationToken);

    public Task<string?> InsertRequestAsync(NewModeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WriteAsync(
            "INSERT INTO execution.promotion_requests (id, account_id, from_mode, to_mode, requested_by, reason, eligible_from, expires_at, evidence, evidence_hash, policy_version) VALUES (@id, @a, @f, @t, @by, @r, @ef, @ex, @ev, @h, @p)",
            c =>
            {
                c.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = request.Id });
                c.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = request.AccountId });
                c.Parameters.Add(new NpgsqlParameter("f", NpgsqlDbType.Varchar) { Value = request.From });
                c.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Varchar) { Value = request.To });
                c.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Varchar) { Value = request.By });
                c.Parameters.Add(new NpgsqlParameter("r", NpgsqlDbType.Varchar) { Value = request.Reason });
                c.Parameters.Add(new NpgsqlParameter("ef", NpgsqlDbType.TimestampTz) { Value = request.EligibleFrom });
                c.Parameters.Add(new NpgsqlParameter("ex", NpgsqlDbType.TimestampTz) { Value = request.ExpiresAt });
                c.Parameters.Add(new NpgsqlParameter("ev", NpgsqlDbType.Jsonb) { Value = request.EvidenceJson });
                c.Parameters.Add(new NpgsqlParameter("h", NpgsqlDbType.Char) { Value = request.EvidenceHash });
                c.Parameters.Add(new NpgsqlParameter("p", NpgsqlDbType.Integer) { Value = ModeGate.PolicyVersion });
            },
            cancellationToken);
    }

    public Task<string?> InsertTrailAsync(NewModeTrailRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        return WriteAsync(
            "INSERT INTO execution.mode_trail (account_id, from_mode, to_mode, kind, actor, reason, request_id, gate_snapshot, gate_hash, effective_from) VALUES (@a, @f, @t, @k, @by, @r, @q, @s, @h, @e)",
            c =>
            {
                c.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = row.AccountId });
                c.Parameters.Add(new NpgsqlParameter("f", NpgsqlDbType.Varchar) { Value = row.From });
                c.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Varchar) { Value = row.To });
                c.Parameters.Add(new NpgsqlParameter("k", NpgsqlDbType.Varchar) { Value = row.Kind });
                c.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Varchar) { Value = row.Actor });
                c.Parameters.Add(new NpgsqlParameter("r", NpgsqlDbType.Varchar) { Value = row.Reason });
                c.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Uuid) { Value = (object?)row.RequestId ?? DBNull.Value });
                c.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Jsonb) { Value = row.SnapshotJson });
                c.Parameters.Add(new NpgsqlParameter("h", NpgsqlDbType.Char) { Value = row.SnapshotHash });
                c.Parameters.Add(new NpgsqlParameter("e", NpgsqlDbType.TimestampTz) { Value = row.EffectiveFrom });
            },
            cancellationToken);
    }

    private Task<ModeRequestRow?> RequestAsync(string sql, NpgsqlDbType type, object key, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<ModeRequestRow?>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(new NpgsqlParameter("k", type) { Value = key });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new ModeRequestRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetString(7).Trim())
                : null;
        }, cancellationToken);

    private Task<string?> WriteAsync(string sql, Action<NpgsqlCommand> bind, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<string?>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(sql, connection);
            bind(command);
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
                return null;
            }
            catch (PostgresException failure) when (failure.MessageText.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return failure.MessageText[Prefix.Length..];
            }
            catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.UniqueViolation)
            {
                return "the database refused the row (" + failure.SqlState + ").";
            }
        }, cancellationToken);
}

/// <summary>The stored facts of the gate, read as the runtime role. A record that does not exist yet is null (the gate stays closed); a database that cannot be read answers null for everything.</summary>
internal sealed class ExecutionModeFacts(XDrishtiDbContext db) : IExecutionModeFacts
{
    public Task<bool?> ExecutionEnabledAsync(CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<bool?>(db, async connection =>
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT execution_enabled FROM execution.settings ORDER BY id DESC LIMIT 1", connection);
                return await command.ExecuteScalarAsync(cancellationToken) is bool enabled && enabled; // no settings row is the default: OFF
            }
            catch (NpgsqlException)
            {
                return null;
            }
        }, cancellationToken);

    public Task<ModeGateFacts> ReadAsync(Guid accountId, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync(db, async connection =>
        {
            bool? role = null, connected = null;
            try
            {
                await using var command = new NpgsqlCommand(
                    "SELECT t.enabled, COALESCE(c.status = 'Connected', false) FROM accounts.broker_accounts a JOIN accounts.trading_settings t ON t.account_id = a.id LEFT JOIN accounts.broker_connections c ON c.account_id = a.id WHERE a.id = @a AND a.archived_at IS NULL", connection);
                command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId });
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    (role, connected) = (reader.GetBoolean(0), reader.GetBoolean(1));
                }
            }
            catch (NpgsqlException)
            {
                // Unreadable stays null: never a pass.
            }

            // The sandbox suite result (P12-02) and the static-IP attestation (P12-11) have no stored record yet: null keeps the gate closed until they are built.
            return new ModeGateFacts(await ExecutionEnabledAsync(cancellationToken), role, connected, null, null, null);
        }, cancellationToken);
}
