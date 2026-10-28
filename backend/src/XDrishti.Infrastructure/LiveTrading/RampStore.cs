using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>The ramp events (<c>trading.ramp_events</c>, spec P11-04): raw SQL, insert and select only. The state of an account is its newest row. The table's guard trigger checks who may write which kind, the step order and the exit confirmation before S3.</summary>
internal sealed class RampStore(NpgsqlDataSource dataSource) : IRampStore
{
    private const string Columns = "id, account_id, kind, from_step, to_step, hold, actor, reason, gate_hash, created_at";

    public async ValueTask<RampState?> CurrentAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT id, to_step, hold FROM trading.ramp_events WHERE account_id = @a ORDER BY id DESC LIMIT 1");
        command.Parameters.AddWithValue("a", NpgsqlDbType.Uuid, accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new RampState(reader.GetInt64(0), reader.GetInt16(1), reader.GetBoolean(2)) : null;
    }

    public async ValueTask<IReadOnlyDictionary<Guid, RampState>> CurrentAllAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT DISTINCT ON (account_id) account_id, id, to_step, hold FROM trading.ramp_events ORDER BY account_id, id DESC");
        var states = new Dictionary<Guid, RampState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states[reader.GetGuid(0)] = new RampState(reader.GetInt64(1), reader.GetInt16(2), reader.GetBoolean(3));
        }

        return states;
    }

    public async ValueTask<IReadOnlyList<StoredRampEvent>> HistoryAsync(Guid accountId, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM trading.ramp_events WHERE account_id = @a ORDER BY id DESC LIMIT @take");
        command.Parameters.AddWithValue("a", NpgsqlDbType.Uuid, accountId);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        var rows = new List<StoredRampEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<StoredRampEvent?> AppendAsync(RampEvent ramp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ramp);
        try
        {
            await using var command = dataSource.CreateCommand($"""
                INSERT INTO trading.ramp_events (account_id, kind, from_step, to_step, hold, actor, reason)
                VALUES (@a, @kind, @from, @to, @hold, @actor, @reason) RETURNING {Columns}
                """);
            command.Parameters.AddWithValue("a", NpgsqlDbType.Uuid, ramp.AccountId);
            command.Parameters.AddWithValue("kind", NpgsqlDbType.Varchar, ramp.Kind);
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Smallint) { Value = ramp.FromStep is { } f ? (short)f : DBNull.Value });
            command.Parameters.AddWithValue("to", NpgsqlDbType.Smallint, (short)ramp.ToStep);
            command.Parameters.AddWithValue("hold", NpgsqlDbType.Boolean, ramp.Hold);
            command.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, ramp.Actor);
            command.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, ramp.Reason);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return Read(reader);
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return null; // the step moved meanwhile, or a rule of the table says no
        }
    }

    private static StoredRampEvent Read(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetGuid(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt16(3), r.GetInt16(4), r.GetBoolean(5), r.GetString(6), r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetFieldValue<DateTimeOffset>(9));
}
