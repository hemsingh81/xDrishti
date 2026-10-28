using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Infrastructure.LiveTrading;

/// <summary>
/// The live switches (<c>trading.live_settings</c>, spec P11-04): raw SQL, insert and select only; the newest row (highest id, drawn by the stamp trigger under the exclusive lock) is current. The hash and the time are stamped by the trigger, which also takes the lock
/// that automatic approvals of live tickets share. No row means both OFF.
/// </summary>
internal sealed class LiveSettingsStore(NpgsqlDataSource dataSource) : ILiveSettingsStore
{
    private const string Columns = "id, snapshot_hash, auto_approve_tickets, auto_ramp_up, actor, reason, changed_at";

    public async ValueTask<LiveSettings> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM trading.live_settings ORDER BY id DESC LIMIT 1");
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return LiveSettings.Defaults;
        }

        var row = Read(reader);
        return new LiveSettings(row.Id, row.Hash, row.AutoApproveTickets, row.AutoRampUp);
    }

    public async ValueTask<IReadOnlyList<StoredLiveSettings>> HistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using var select = dataSource.CreateCommand($"SELECT {Columns} FROM trading.live_settings ORDER BY id DESC LIMIT @limit");
        select.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Clamp(limit, 1, 200));
        var rows = new List<StoredLiveSettings>();
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Read(reader));
        }

        return rows;
    }

    public async ValueTask<StoredLiveSettings?> AppendAsync(string actor, bool autoApproveTickets, bool autoRampUp, string reason, long? expectedCurrentId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The lock the insert trigger takes, taken first so the look at the newest row and the insert cannot be interleaved with another change.
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('trading.live_settings', 0))", connection, transaction))
        {
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var newest = new NpgsqlCommand("SELECT id FROM trading.live_settings ORDER BY id DESC LIMIT 1", connection, transaction))
        {
            long? current = await newest.ExecuteScalarAsync(cancellationToken) is long id ? id : null;
            if (current != expectedCurrentId)
            {
                return null;
            }
        }

        await using var insert = new NpgsqlCommand($"INSERT INTO trading.live_settings (auto_approve_tickets, auto_ramp_up, actor, reason) VALUES (@auto, @ramp, @actor, @reason) RETURNING {Columns}", connection, transaction);
        insert.Parameters.AddWithValue("auto", NpgsqlDbType.Boolean, autoApproveTickets);
        insert.Parameters.AddWithValue("ramp", NpgsqlDbType.Boolean, autoRampUp);
        insert.Parameters.AddWithValue("actor", NpgsqlDbType.Varchar, actor);
        insert.Parameters.AddWithValue("reason", NpgsqlDbType.Varchar, reason);
        StoredLiveSettings row;
        await using (var reader = await insert.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            row = Read(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return row;
    }

    private static StoredLiveSettings Read(NpgsqlDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetBoolean(2), r.GetBoolean(3), r.GetString(4), r.GetString(5), r.GetFieldValue<DateTimeOffset>(6));
}
