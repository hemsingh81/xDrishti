using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Execution;
using XDrishti.Domain.Execution.Limits;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>
/// The limits record as the runtime role (spec P12-09, ADR 0167): SELECT on <c>execution.limits</c> and INSERT of the owner's snapshot, which the trigger <c>execution.check_limits</c> judges again (owner only, a raise never sooner than
/// 09:00 India time of the next day, a lowering stamped now, the ceilings by constraint). Its message comes back as the refusal.
/// </summary>
internal sealed class ExecutionLimitsAccess(XDrishtiDbContext db) : IExecutionLimitsAccess
{
    private const string Prefix = "execution.limits_refused: ";

    private const string Select = """
        SELECT id, account_id, max_orders_per_day, max_notional_per_order, max_notional_per_day, max_loss_per_day, max_open_positions, allow_list::text, window_start, window_end, products, exit_all_on_loss_halt, actor, reason, changed_at,
               effective_from, snapshot_hash
          FROM execution.limits
        """;

    public Task<DateTimeOffset> NowAsync(CancellationToken cancellationToken) => DatabaseClock.NowAsync(db, cancellationToken);

    public Task<bool?> TradingOnAsync(Guid accountId, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<bool?>(db, async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT t.enabled AND a.archived_at IS NULL FROM accounts.broker_accounts a JOIN accounts.trading_settings t ON t.account_id = a.id WHERE a.id = @a", connection);
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId });
            return await command.ExecuteScalarAsync(cancellationToken) is bool on ? on : null;
        }, cancellationToken);

    public Task<IReadOnlyList<StoredLimits>> RowsAsync(Guid accountId, int take, CancellationToken cancellationToken) =>
        ExecutionConnection.UseAsync<IReadOnlyList<StoredLimits>>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(Select + " WHERE account_id = @a ORDER BY id DESC LIMIT @n", connection);
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = accountId });
            command.Parameters.Add(new NpgsqlParameter("n", NpgsqlDbType.Integer) { Value = take });
            var rows = new List<StoredLimits>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var sheet = new ExecutionLimits(
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                    reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                    reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    JsonSerializer.Deserialize<string[]>(reader.GetString(7)) ?? [],
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<TimeOnly>(8),
                    reader.IsDBNull(9) ? null : reader.GetFieldValue<TimeOnly>(9),
                    reader.GetFieldValue<string[]>(10),
                    reader.GetBoolean(11));
                rows.Add(new StoredLimits(reader.GetInt64(0), reader.GetGuid(1), sheet, reader.GetString(12), reader.GetString(13), reader.GetFieldValue<DateTimeOffset>(14), reader.GetFieldValue<DateTimeOffset>(15), reader.GetString(16).Trim()));
            }

            return rows;
        }, cancellationToken);

    public Task<(long? Id, string? Refused)> InsertAsync(NewLimits row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        return ExecutionConnection.UseAsync<(long?, string?)>(db, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO execution.limits (account_id, max_orders_per_day, max_notional_per_order, max_notional_per_day, max_loss_per_day, max_open_positions, allow_list, window_start, window_end, products, exit_all_on_loss_halt, actor, reason,
                                              effective_from, snapshot_hash)
                VALUES (@a, @o, @no, @nd, @l, @p, @al, @ws, @we, @pr, @x, @by, @r, @e, @h)
                RETURNING id
                """, connection);
            var s = row.Limits;
            command.Parameters.Add(new NpgsqlParameter("a", NpgsqlDbType.Uuid) { Value = row.AccountId });
            command.Parameters.Add(new NpgsqlParameter("o", NpgsqlDbType.Integer) { Value = (object?)s.MaxOrdersPerDay ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("no", NpgsqlDbType.Numeric) { Value = (object?)s.MaxNotionalPerOrder ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("nd", NpgsqlDbType.Numeric) { Value = (object?)s.MaxNotionalPerDay ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("l", NpgsqlDbType.Numeric) { Value = (object?)s.MaxLossPerDay ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("p", NpgsqlDbType.Integer) { Value = (object?)s.MaxOpenPositions ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("al", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(s.AllowList) });
            command.Parameters.Add(new NpgsqlParameter("ws", NpgsqlDbType.Time) { Value = (object?)s.WindowStart ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("we", NpgsqlDbType.Time) { Value = (object?)s.WindowEnd ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("pr", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = s.Products.ToArray() });
            command.Parameters.Add(new NpgsqlParameter("x", NpgsqlDbType.Boolean) { Value = s.ExitAllOnLossHalt });
            command.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Varchar) { Value = row.Actor });
            command.Parameters.Add(new NpgsqlParameter("r", NpgsqlDbType.Varchar) { Value = row.Reason });
            command.Parameters.Add(new NpgsqlParameter("e", NpgsqlDbType.TimestampTz) { Value = row.EffectiveFrom.ToUniversalTime() });
            command.Parameters.Add(new NpgsqlParameter("h", NpgsqlDbType.Char) { Value = row.SnapshotHash });
            try
            {
                return ((long?)await command.ExecuteScalarAsync(cancellationToken), (string?)null);
            }
            catch (PostgresException failure) when (failure.MessageText.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return (null, failure.MessageText[Prefix.Length..]);
            }
            catch (PostgresException failure) when (failure.SqlState is PostgresErrorCodes.CheckViolation or PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.UniqueViolation)
            {
                return (null, "the database refused the row (" + failure.SqlState + ")");
            }
        }, cancellationToken);
    }
}
