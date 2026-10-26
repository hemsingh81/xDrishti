using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Depth;

namespace XDrishti.Infrastructure.Depth;

/// <summary>
/// The write side of the depth tables (P7-16): snapshots go in with <c>COPY</c> in one transaction per batch; the day's set and the end row are small inserts. <b>It has no method that reads a depth row</b> (an architecture test keeps it so); it reads the
/// stored size (<c>hypertable_detailed_size</c>) and the free bytes of the file system the process runs on (<see cref="DriveInfo"/>, a <c>statvfs</c> of the container root). The runtime role can insert and read but not update, delete or truncate.
/// </summary>
internal sealed class DepthStore(NpgsqlDataSource dataSource) : IDepthStore
{
    private const string Copy = "COPY market.depth_snapshots (ts, instrument_id, seq, rx_mono_ms, flags, bid_px, bid_qty, bid_ord, ask_px, ask_qty, ask_ord) FROM STDIN (FORMAT BINARY)";

    private const NpgsqlDbType IntArray = NpgsqlDbType.Array | NpgsqlDbType.Integer;

    public async ValueTask<int> AppendAsync(IReadOnlyList<DepthSnapshot> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var importer = await connection.BeginBinaryImportAsync(Copy, cancellationToken))
        {
            foreach (var row in rows)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(row.Ts.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(row.InstrumentId, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(row.Seq, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(row.RxMonoMs, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(row.Flags, NpgsqlDbType.Smallint, cancellationToken);
                await importer.WriteAsync(row.BidPx, IntArray, cancellationToken);
                await importer.WriteAsync(row.BidQty, IntArray, cancellationToken);
                await importer.WriteAsync(row.BidOrd, IntArray, cancellationToken);
                await importer.WriteAsync(row.AskPx, IntArray, cancellationToken);
                await importer.WriteAsync(row.AskQty, IntArray, cancellationToken);
                await importer.WriteAsync(row.AskOrd, IntArray, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    public async ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT COALESCE(sum(total_bytes), 0)::bigint FROM hypertable_detailed_size('market.depth_snapshots')");
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    public ValueTask<long?> FreeBytesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return ValueTask.FromResult<long?>(new DriveInfo("/").AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            return ValueTask.FromResult<long?>(null);
        }
    }

    public async ValueTask<IReadOnlyList<DepthSetEntry>> LoadSetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT s.instrument_id, b.broker_id, i.canonical_symbol, s.reason, s.rank
            FROM market.depth_session_instruments s
            JOIN market.instruments i ON i.id = s.instrument_id
            JOIN market.instrument_broker_ids b ON b.instrument_id = i.id AND b.broker = 'dhan' AND b.segment = 'NSE_EQ'
            WHERE s.session_date = @date
            ORDER BY s.rank
            """);
        command.Parameters.AddWithValue("date", date);
        var set = new List<DepthSetEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (int.TryParse(reader.GetString(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var securityId))
            {
                set.Add(new DepthSetEntry(reader.GetGuid(0), securityId, reader.GetString(2), reader.GetString(3), reader.GetInt32(4)));
            }
        }

        return set;
    }

    public async ValueTask SaveSetAsync(DateOnly date, IReadOnlyList<DepthSetEntry> set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var entry in set)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO market.depth_session_instruments (session_date, instrument_id, reason, rank) VALUES (@date, @id, @reason, @rank) ON CONFLICT DO NOTHING",
                connection,
                transaction);
            command.Parameters.AddWithValue("date", date);
            command.Parameters.AddWithValue("id", entry.InstrumentId);
            command.Parameters.AddWithValue("reason", entry.Reason);
            command.Parameters.AddWithValue("rank", entry.Rank);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<bool> RecordSessionAsync(DepthSessionRecord session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO market.depth_sessions (session_date, started_at, ended_at, end_reason, instruments, snapshots, packets, dropped_rows, gap_seconds, stored_bytes, note)
            VALUES (@date, @started, @ended, @reason, @instruments, @snapshots, @packets, @dropped, @gap, @stored, @note)
            ON CONFLICT (session_date) DO NOTHING
            """);
        command.Parameters.AddWithValue("date", session.Date);
        command.Parameters.AddWithValue("started", session.StartedAt.UtcDateTime);
        command.Parameters.AddWithValue("ended", session.EndedAt.UtcDateTime);
        command.Parameters.AddWithValue("reason", session.EndReason);
        command.Parameters.AddWithValue("instruments", session.Instruments);
        command.Parameters.AddWithValue("snapshots", session.Snapshots);
        command.Parameters.AddWithValue("packets", session.Packets);
        command.Parameters.AddWithValue("dropped", session.DroppedRows);
        command.Parameters.AddWithValue("gap", session.GapSeconds);
        command.Parameters.AddWithValue("stored", (object?)session.StoredBytes ?? DBNull.Value);
        command.Parameters.AddWithValue("note", (object?)session.Note ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async ValueTask<IReadOnlyList<DateOnly>> FindUnfinishedDaysAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT DISTINCT i.session_date
            FROM market.depth_session_instruments i
            WHERE NOT EXISTS (SELECT 1 FROM market.depth_sessions s WHERE s.session_date = i.session_date)
            ORDER BY 1
            """);
        var days = new List<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(DateOnly.FromDateTime(reader.GetDateTime(0)));
        }

        return days;
    }
}
