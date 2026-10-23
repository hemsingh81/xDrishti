using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Domain.Autonomy;

namespace XDrishti.Infrastructure.Planning;

/// <summary>
/// The autonomy settings behind <see cref="IAutonomySettingsStore"/> (spec P7-12, ADR 0091): raw SQL, insert and select only. A change is a new row; the newest (the highest id, which the insert trigger draws under the exclusive lock, so id order is commit order and no clock step can reorder it) is current. The hash and the time are stamped by a trigger,
/// which also takes the lock that automatic approvals share. No row means the defaults, everything OFF.
/// </summary>
internal sealed class AutonomySettingsStore(NpgsqlDataSource dataSource) : IAutonomySettingsStore
{
    private const string Columns = "id, snapshot_hash, auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, actor, reason, changed_at";

    public async ValueTask<AutonomySettings> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM workflow.autonomy_settings ORDER BY id DESC LIMIT 1");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return AutonomySettings.Defaults;
        }

        var row = Read(reader);
        return new AutonomySettings(row.Id, row.Hash, row.AutoApproveTickets, row.PromoteFromReserve, row.MinPWin, row.MaxAutoPerDay);
    }

    public async ValueTask<IReadOnlyList<AutonomyChangeRow>> HistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM workflow.autonomy_settings ORDER BY id DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        var rows = new List<AutonomyChangeRow>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<AutonomyChangeRow?> AppendAsync(AutonomyChange change, long? expectedCurrentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The same lock the insert trigger takes, taken first so the check of the newest row and the insert cannot be interleaved with another change.
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('workflow.autonomy_settings', 0))", connection, transaction))
        {
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var newest = new NpgsqlCommand("SELECT id FROM workflow.autonomy_settings ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            long? current = await newest.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
            if (current != expectedCurrentId)
            {
                return null;
            }
        }

        await using var insert = new NpgsqlCommand($"""
            INSERT INTO workflow.autonomy_settings (auto_approve_tickets, promote_from_reserve, min_p_win, max_auto_per_day, actor, reason)
            VALUES (@auto, @reserve, @min, @max, @actor, @reason) RETURNING {Columns}
            """, connection, transaction);
        var p = insert.Parameters;
        p.AddWithValue("auto", NpgsqlDbType.Boolean, change.AutoApproveTickets);
        p.AddWithValue("reserve", NpgsqlDbType.Boolean, change.PromoteFromReserve);
        p.AddWithValue("min", NpgsqlDbType.Numeric, (decimal)change.MinPWin);
        p.AddWithValue("max", NpgsqlDbType.Integer, change.MaxAutoPerDay);
        p.AddWithValue("actor", NpgsqlDbType.Varchar, change.Actor);
        p.AddWithValue("reason", NpgsqlDbType.Varchar, change.Reason);
        AutonomyChangeRow row;
        await using (var reader = await insert.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            row = Read(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return row;
    }

    private static AutonomyChangeRow Read(NpgsqlDataReader reader) =>
        new(reader.GetInt64(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), (double)reader.GetDecimal(4), reader.GetInt32(5), reader.GetString(6), reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8));
}
