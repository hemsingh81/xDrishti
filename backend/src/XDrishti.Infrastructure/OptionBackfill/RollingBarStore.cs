using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Infrastructure.OptionBackfill;

/// <summary>
/// The write side of <c>options.rolling_bars</c> and <c>options.fetch_log</c> (P14-03, ADR 0177): the attempt's fetch-log row and its bars go in one transaction; the bars are copied (binary COPY) into a temporary table and inserted with
/// <c>ON CONFLICT DO NOTHING</c> on the primary key, so a window fetched twice inserts nothing the second time. <b>It has no method that reads a bar</b> (an architecture test keeps it so); it reads the stored size (<c>hypertable_detailed_size</c>)
/// and the free bytes of the file system the process runs on. The runtime role can insert and read but not update, delete or truncate.
/// </summary>
internal sealed class RollingBarStore(NpgsqlDataSource dataSource) : IRollingBarStore
{
    private const string CreateStage =
        """
        CREATE TEMP TABLE stage_rolling_bars (
            ts timestamptz NOT NULL, underlying_id uuid NOT NULL, expiry_flag char(1) NOT NULL, expiry_code smallint NOT NULL, atm_offset smallint NOT NULL, side char(2) NOT NULL, interval_minutes smallint NOT NULL,
            strike_px integer NOT NULL, spot_px integer NOT NULL, open_px integer NOT NULL, high_px integer NOT NULL, low_px integer NOT NULL, close_px integer NOT NULL,
            volume bigint NULL, oi bigint NULL, iv real NULL, flags smallint NOT NULL
        ) ON COMMIT DROP
        """;

    private const string CopyStage =
        "COPY stage_rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, volume, oi, iv, flags) FROM STDIN (FORMAT BINARY)";

    private const string InsertBars =
        """
        INSERT INTO options.rolling_bars (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, volume, oi, iv, flags)
        SELECT ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, strike_px, spot_px, open_px, high_px, low_px, close_px, volume, oi, iv, flags
        FROM stage_rolling_bars
        ON CONFLICT (ts, underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes) DO NOTHING
        """;

    private const string InsertAttempt =
        """
        INSERT INTO options.fetch_log (underlying_id, expiry_flag, expiry_code, atm_offset, side, interval_minutes, window_from, window_to, attempt, started_at, ended_at, outcome, http_status, dhan_code, bars, latency_ms)
        VALUES (@underlying, @flag, @code, @offset, @side, @interval, @from, @to, @attempt, @started, @ended, @outcome, @status, @dhan, @bars, @latency)
        """;

    public async ValueTask<int> AppendAsync(BackfillFetchRecord attempt, IReadOnlyList<BackfillBar> bars, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(bars);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var inserted = 0;
        if (bars.Count > 0)
        {
            await using (var create = new NpgsqlCommand(CreateStage, connection, transaction))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var importer = await connection.BeginBinaryImportAsync(CopyStage, cancellationToken))
            {
                foreach (var bar in bars)
                {
                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(bar.Ts.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(bar.UnderlyingId, NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(bar.ExpiryFlag.ToString(), NpgsqlDbType.Char, cancellationToken);
                    await importer.WriteAsync((short)bar.ExpiryCode, NpgsqlDbType.Smallint, cancellationToken);
                    await importer.WriteAsync((short)bar.Offset, NpgsqlDbType.Smallint, cancellationToken);
                    await importer.WriteAsync(bar.Side, NpgsqlDbType.Char, cancellationToken);
                    await importer.WriteAsync((short)bar.IntervalMinutes, NpgsqlDbType.Smallint, cancellationToken);
                    await importer.WriteAsync(bar.StrikePx, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(bar.SpotPx, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(bar.OpenPx, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(bar.HighPx, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(bar.LowPx, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(bar.ClosePx, NpgsqlDbType.Integer, cancellationToken);
                    await WriteAsync(importer, bar.Volume, NpgsqlDbType.Bigint, cancellationToken);
                    await WriteAsync(importer, bar.Oi, NpgsqlDbType.Bigint, cancellationToken);
                    await WriteAsync(importer, bar.Iv, NpgsqlDbType.Real, cancellationToken);
                    await importer.WriteAsync(bar.Flags, NpgsqlDbType.Smallint, cancellationToken);
                }

                await importer.CompleteAsync(cancellationToken);
            }

            // shortcut: an insert into a compressed chunk decompresses the batches it touches, bounded by timescaledb.max_tuples_decompressed_per_dml_transaction (default 100,000; one 30-day segment is about 33,000 rows), and the
            // decompressed batches stay decompressed until the next compression policy run (ADR 0177). A statement over the limit fails and is handled as a store failure.
            await using var insert = new NpgsqlCommand(InsertBars, connection, transaction);
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var log = new NpgsqlCommand(InsertAttempt, connection, transaction))
        {
            var key = attempt.Key;
            log.Parameters.AddWithValue("underlying", key.UnderlyingId);
            log.Parameters.Add(new NpgsqlParameter("flag", NpgsqlDbType.Char) { Value = key.ExpiryFlag.ToString() });
            log.Parameters.Add(new NpgsqlParameter("code", NpgsqlDbType.Smallint) { Value = (short)key.ExpiryCode });
            log.Parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Smallint) { Value = (short)key.Offset });
            log.Parameters.Add(new NpgsqlParameter("side", NpgsqlDbType.Char) { Value = key.Side });
            log.Parameters.Add(new NpgsqlParameter("interval", NpgsqlDbType.Smallint) { Value = (short)key.IntervalMinutes });
            log.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = key.From });
            log.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = key.To });
            log.Parameters.Add(new NpgsqlParameter("attempt", NpgsqlDbType.Smallint) { Value = (short)attempt.Attempt });
            log.Parameters.AddWithValue("started", attempt.StartedAt.UtcDateTime);
            log.Parameters.AddWithValue("ended", attempt.EndedAt.UtcDateTime);
            log.Parameters.AddWithValue("outcome", attempt.Outcome);
            log.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Smallint) { Value = attempt.HttpStatus.HasValue ? (short)attempt.HttpStatus.Value : DBNull.Value });
            log.Parameters.Add(new NpgsqlParameter("dhan", NpgsqlDbType.Varchar) { Value = (object?)attempt.DhanCode ?? DBNull.Value });
            log.Parameters.AddWithValue("bars", attempt.Bars);
            log.Parameters.AddWithValue("latency", attempt.LatencyMs);
            await log.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    public async ValueTask<long> StoredBytesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT COALESCE(sum(total_bytes), 0)::bigint FROM hypertable_detailed_size('options.rolling_bars')");
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
