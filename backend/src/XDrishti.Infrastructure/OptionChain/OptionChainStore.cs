using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionChain;

namespace XDrishti.Infrastructure.OptionChain;

/// <summary>
/// The write side of the option-chain tables (P14-02, ADR 0176): the request row and the snapshot rows of a request go in one transaction; the snapshots are copied (binary COPY) into a temporary table and inserted with
/// <c>ON CONFLICT DO NOTHING</c> on the primary key, so a re-run of a slot inserts nothing. <b>It has no method that reads a snapshot row</b> (an architecture test keeps it so); it reads the stored size (<c>hypertable_detailed_size</c>), the free bytes of
/// the file system the process runs on and the dates of days that have a request row and no end row. The runtime role can insert and read but not update, delete or truncate.
/// </summary>
internal sealed class OptionChainStore(NpgsqlDataSource dataSource) : IOptionChainStore
{
    private const string CreateStage =
        """
        CREATE TEMP TABLE stage_chain_snapshots (
            ts timestamptz NOT NULL, underlying_id uuid NOT NULL, expiry date NOT NULL, strike_px integer NOT NULL, side char(2) NOT NULL,
            rx_at timestamptz NOT NULL, security_id bigint NOT NULL, underlying_px integer NOT NULL,
            last_px integer NULL, avg_px integer NULL, prev_close_px integer NULL,
            bid_px integer NULL, bid_qty integer NULL, ask_px integer NULL, ask_qty integer NULL,
            oi bigint NULL, prev_oi bigint NULL, volume bigint NULL, prev_volume bigint NULL,
            iv real NULL, delta real NULL, gamma real NULL, theta real NULL, vega real NULL, flags smallint NOT NULL
        ) ON COMMIT DROP
        """;

    private const string CopyStage =
        "COPY stage_chain_snapshots (ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, last_px, avg_px, prev_close_px, bid_px, bid_qty, ask_px, ask_qty, oi, prev_oi, volume, prev_volume, iv, delta, gamma, theta, vega, flags) FROM STDIN (FORMAT BINARY)";

    private const string InsertSnapshots =
        """
        INSERT INTO options.chain_snapshots (ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, last_px, avg_px, prev_close_px, bid_px, bid_qty, ask_px, ask_qty, oi, prev_oi, volume, prev_volume, iv, delta, gamma, theta, vega, flags)
        SELECT ts, underlying_id, expiry, strike_px, side, rx_at, security_id, underlying_px, last_px, avg_px, prev_close_px, bid_px, bid_qty, ask_px, ask_qty, oi, prev_oi, volume, prev_volume, iv, delta, gamma, theta, vega, flags
        FROM stage_chain_snapshots
        ON CONFLICT (ts, underlying_id, expiry, strike_px, side) DO NOTHING
        """;

    private const string InsertRequest =
        """
        INSERT INTO options.chain_requests (slot_ts, kind, underlying_id, expiry, outcome, http_status, dhan_code, rows, latency_ms, requested_at)
        VALUES (@slot, @kind, @underlying, @expiry, @outcome, @status, @code, @rows, @latency, @at)
        """;

    public async ValueTask<int> AppendAsync(ChainRequestRecord request, IReadOnlyList<ChainSnapshotRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var inserted = 0;
        if (rows.Count > 0)
        {
            await using (var create = new NpgsqlCommand(CreateStage, connection, transaction))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var importer = await connection.BeginBinaryImportAsync(CopyStage, cancellationToken))
            {
                foreach (var row in rows)
                {
                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(row.Ts.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(row.UnderlyingId, NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(row.Expiry, NpgsqlDbType.Date, cancellationToken);
                    await importer.WriteAsync(row.StrikePx, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(row.Side, NpgsqlDbType.Char, cancellationToken);
                    await importer.WriteAsync(row.RxAt.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(row.SecurityId, NpgsqlDbType.Bigint, cancellationToken);
                    await importer.WriteAsync(row.UnderlyingPx, NpgsqlDbType.Integer, cancellationToken);
                    await WriteAsync(importer, row.LastPx, NpgsqlDbType.Integer, cancellationToken);
                    await WriteAsync(importer, row.AvgPx, NpgsqlDbType.Integer, cancellationToken);
                    await WriteAsync(importer, row.PrevClosePx, NpgsqlDbType.Integer, cancellationToken);
                    await WriteAsync(importer, row.BidPx, NpgsqlDbType.Integer, cancellationToken);
                    await WriteAsync(importer, row.BidQty, NpgsqlDbType.Integer, cancellationToken);
                    await WriteAsync(importer, row.AskPx, NpgsqlDbType.Integer, cancellationToken);
                    await WriteAsync(importer, row.AskQty, NpgsqlDbType.Integer, cancellationToken);
                    await WriteAsync(importer, row.Oi, NpgsqlDbType.Bigint, cancellationToken);
                    await WriteAsync(importer, row.PrevOi, NpgsqlDbType.Bigint, cancellationToken);
                    await WriteAsync(importer, row.Volume, NpgsqlDbType.Bigint, cancellationToken);
                    await WriteAsync(importer, row.PrevVolume, NpgsqlDbType.Bigint, cancellationToken);
                    await WriteAsync(importer, row.Iv, NpgsqlDbType.Real, cancellationToken);
                    await WriteAsync(importer, row.Delta, NpgsqlDbType.Real, cancellationToken);
                    await WriteAsync(importer, row.Gamma, NpgsqlDbType.Real, cancellationToken);
                    await WriteAsync(importer, row.Theta, NpgsqlDbType.Real, cancellationToken);
                    await WriteAsync(importer, row.Vega, NpgsqlDbType.Real, cancellationToken);
                    await importer.WriteAsync(row.Flags, NpgsqlDbType.Smallint, cancellationToken);
                }

                await importer.CompleteAsync(cancellationToken);
            }

            await using var insert = new NpgsqlCommand(InsertSnapshots, connection, transaction);
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var log = new NpgsqlCommand(InsertRequest, connection, transaction))
        {
            log.Parameters.AddWithValue("slot", request.Slot.UtcDateTime);
            log.Parameters.AddWithValue("kind", request.Kind);
            log.Parameters.AddWithValue("underlying", request.UnderlyingId);
            log.Parameters.Add(new NpgsqlParameter("expiry", NpgsqlDbType.Date) { Value = request.Expiry.HasValue ? request.Expiry.Value : DBNull.Value });
            log.Parameters.AddWithValue("outcome", request.Outcome);
            log.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Smallint) { Value = request.HttpStatus.HasValue ? (short)request.HttpStatus.Value : DBNull.Value });
            log.Parameters.Add(new NpgsqlParameter("code", NpgsqlDbType.Varchar) { Value = (object?)request.DhanCode ?? DBNull.Value });
            log.Parameters.AddWithValue("rows", inserted);
            log.Parameters.AddWithValue("latency", request.LatencyMs);
            log.Parameters.AddWithValue("at", request.At.UtcDateTime);
            await log.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT COALESCE(sum(total_bytes), 0)::bigint FROM hypertable_detailed_size('options.chain_snapshots')");
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

    public async ValueTask<bool> RecordSessionAsync(ChainSessionRecord session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO options.chain_sessions (session_date, started_at, ended_at, end_reason, slots, requests, ok, gaps, rows, stored_bytes, note)
            VALUES (@date, @started, @ended, @reason, @slots, @requests, @ok, @gaps, @rows, @stored, @note)
            ON CONFLICT (session_date) DO NOTHING
            """);
        command.Parameters.AddWithValue("date", session.Date);
        command.Parameters.AddWithValue("started", session.StartedAt.UtcDateTime);
        command.Parameters.AddWithValue("ended", session.EndedAt.UtcDateTime);
        command.Parameters.AddWithValue("reason", session.EndReason);
        command.Parameters.AddWithValue("slots", session.Slots);
        command.Parameters.AddWithValue("requests", session.Requests);
        command.Parameters.AddWithValue("ok", session.Ok);
        command.Parameters.AddWithValue("gaps", session.Gaps);
        command.Parameters.AddWithValue("rows", session.Rows);
        command.Parameters.Add(new NpgsqlParameter("stored", NpgsqlDbType.Bigint) { Value = session.StoredBytes.HasValue ? session.StoredBytes.Value : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("note", NpgsqlDbType.Varchar) { Value = (object?)session.Note ?? DBNull.Value });
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async ValueTask<IReadOnlyList<DateOnly>> FindUnfinishedDaysAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT DISTINCT r.d
            FROM (SELECT ((slot_ts AT TIME ZONE 'UTC') + interval '5 hours 30 minutes')::date AS d FROM options.chain_requests) r
            WHERE NOT EXISTS (SELECT 1 FROM options.chain_sessions s WHERE s.session_date = r.d)
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

    private static async ValueTask WriteAsync<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct
    {
        if (value is { } present)
        {
            await importer.WriteAsync(present, type, cancellationToken);
        }
        else
        {
            await importer.WriteNullAsync(cancellationToken);
        }
    }
}
