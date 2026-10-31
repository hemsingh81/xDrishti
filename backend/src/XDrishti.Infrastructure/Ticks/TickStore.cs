using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Ticks;

namespace XDrishti.Infrastructure.Ticks;

/// <summary>
/// The write side of the tick tables (P7-16b): rows go in with <c>COPY</c> in one transaction per batch; the day's set and the end row are small inserts. <b>It has no method that reads a tick row</b> (an architecture test keeps it so); it reads the
/// stored size (<c>hypertable_detailed_size</c>), the free bytes of the file system the process runs on and the depth recorder's stored set of a day (read only). It <b>never writes a depth table</b>. The runtime role can insert and read but not update,
/// delete or truncate.
/// </summary>
internal sealed class TickStore(NpgsqlDataSource dataSource) : ITickStore
{
    private const string Copy = "COPY market.trade_ticks (ts, instrument_id, rx_mono_ms, rx_seq, ltt_epoch, ltp_px, ltq, cum_vol, flags) FROM STDIN (FORMAT BINARY)";

    /// <summary>The depth recorder's set of a day, read only (the tick recorder never writes a depth table).</summary>
    private const string DepthSetQuery =
        """
        SELECT s.instrument_id, b.broker_id, i.canonical_symbol, 'depth_set', s.reason, s.rank
        FROM market.depth_session_instruments s
        JOIN market.instruments i ON i.id = s.instrument_id
        JOIN market.instrument_broker_ids b ON b.instrument_id = i.id AND b.broker = 'dhan' AND b.segment = 'NSE_EQ'
        WHERE s.session_date = @date
        ORDER BY s.rank
        """;

    private const string TickSetQuery =
        """
        SELECT s.instrument_id, b.broker_id, i.canonical_symbol, s.source, s.reason, s.rank
        FROM market.tick_session_instruments s
        JOIN market.instruments i ON i.id = s.instrument_id
        JOIN market.instrument_broker_ids b ON b.instrument_id = i.id AND b.broker = 'dhan' AND b.segment = 'NSE_EQ'
        WHERE s.session_date = @date
        ORDER BY s.rank
        """;

    public async ValueTask<int> AppendAsync(IReadOnlyList<TradeTick> rows, CancellationToken cancellationToken)
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
                await importer.WriteAsync(row.RxMonoMs, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(row.RxSeq, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(row.LttEpoch, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(row.LtpPx, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(row.Ltq, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(row.CumVol, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(row.Flags, NpgsqlDbType.Smallint, cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    public async ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT COALESCE(sum(total_bytes), 0)::bigint FROM hypertable_detailed_size('market.trade_ticks')");
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

    public ValueTask<IReadOnlyList<TickSetEntry>> LoadDepthSetAsync(DateOnly date, CancellationToken cancellationToken) =>
        LoadAsync(DepthSetQuery, date, cancellationToken);

    public ValueTask<IReadOnlyList<TickSetEntry>> LoadSetAsync(DateOnly date, CancellationToken cancellationToken) =>
        LoadAsync(TickSetQuery, date, cancellationToken);

    public async ValueTask SaveSetAsync(DateOnly date, IReadOnlyList<TickSetEntry> set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var entry in set)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO market.tick_session_instruments (session_date, instrument_id, source, reason, rank) VALUES (@date, @id, @source, @reason, @rank) ON CONFLICT DO NOTHING",
                connection,
                transaction);
            command.Parameters.AddWithValue("date", date);
            command.Parameters.AddWithValue("id", entry.InstrumentId);
            command.Parameters.AddWithValue("source", entry.Source);
            command.Parameters.AddWithValue("reason", entry.Reason);
            command.Parameters.AddWithValue("rank", entry.Rank);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<bool> RecordSessionAsync(TickSessionRecord session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO market.tick_sessions (session_date, started_at, ended_at, end_reason, instruments, packets, rows, unchanged, dropped_rows, gap_seconds, stored_bytes, note)
            VALUES (@date, @started, @ended, @reason, @instruments, @packets, @rows, @unchanged, @dropped, @gap, @stored, @note)
            ON CONFLICT (session_date) DO NOTHING
            """);
        command.Parameters.AddWithValue("date", session.Date);
        command.Parameters.AddWithValue("started", session.StartedAt.UtcDateTime);
        command.Parameters.AddWithValue("ended", session.EndedAt.UtcDateTime);
        command.Parameters.AddWithValue("reason", session.EndReason);
        command.Parameters.AddWithValue("instruments", session.Instruments);
        command.Parameters.AddWithValue("packets", session.Packets);
        command.Parameters.AddWithValue("rows", session.Rows);
        command.Parameters.AddWithValue("unchanged", session.Unchanged);
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
            FROM market.tick_session_instruments i
            WHERE NOT EXISTS (SELECT 1 FROM market.tick_sessions s WHERE s.session_date = i.session_date)
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

    /// <summary>A day's set from one of the two constant queries above, with the Dhan NSE equity id.</summary>
    private async ValueTask<IReadOnlyList<TickSetEntry>> LoadAsync(string sql, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("date", date);
        var set = new List<TickSetEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (int.TryParse(reader.GetString(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var securityId))
            {
                set.Add(new TickSetEntry(reader.GetGuid(0), securityId, reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5)));
            }
        }

        return set;
    }
}
