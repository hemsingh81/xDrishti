using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>
/// Writes and reads the stored 1-minute bars. A merge streams the bars (binary COPY) into a temporary table and merges them into the hypertable in one
/// transaction together with the instrument's coverage and the CSV file record, so a failure leaves nothing behind (ADR 0016 §3). Never loads a file into memory.
/// </summary>
internal sealed class BarStore(NpgsqlDataSource dataSource, TimeProvider clock) : IBarStore
{
    /// <summary>A merge into compressed chunks can decompress and rewrite many batches: more than the pool's default command timeout.</summary>
    private const int MergeTimeoutSeconds = 900;

    private const string CreateStage =
        """
        CREATE TEMP TABLE stage_bars (
            ts timestamptz NOT NULL,
            open double precision NOT NULL,
            high double precision NOT NULL,
            low double precision NOT NULL,
            close double precision NOT NULL,
            volume bigint NOT NULL
        ) ON COMMIT DROP
        """;

    private const string CopyStage = "COPY stage_bars (ts, open, high, low, close, volume) FROM STDIN (FORMAT BINARY)";

    // What each staged bar would do. Precedence (ADR 0016 §3): a CSV bar never replaces a Dhan bar; a Dhan bar replaces any bar of the minute, and takes over
    // a CSV bar of the same values too ('taken': that is not a change of values, so nothing is aggregated again).
    private const string Classify =
        """
        WITH j AS (
            SELECT s.ts,
                CASE
                    WHEN b.ts IS NULL THEN 'new'
                    WHEN @source = 'csv' AND b.source = 'dhan' THEN 'kept'
                    WHEN (b.open, b.high, b.low, b.close, b.volume) IS DISTINCT FROM (s.open, s.high, s.low, s.close, s.volume) THEN 'changed'
                    WHEN @source = 'dhan' AND b.source <> 'dhan' THEN 'taken'
                    ELSE 'same'
                END AS kind
            FROM stage_bars s
            LEFT JOIN market.bars_1m b ON b.instrument_id = @id AND b.ts = s.ts)
        SELECT count(*) FILTER (WHERE kind = 'new'), count(*) FILTER (WHERE kind = 'changed'), count(*) FILTER (WHERE kind IN ('same', 'taken')),
               count(*) FILTER (WHERE kind = 'kept'), count(*) FILTER (WHERE kind = 'taken'),
               min(ts) FILTER (WHERE kind IN ('new', 'changed')), max(ts) FILTER (WHERE kind IN ('new', 'changed'))
        FROM j
        """;

    // New minutes are inserted without ON CONFLICT: on a hypertable the arbiter makes an insert five times slower, and the classification above has just found
    // which minutes are new (one job writes at a time).
    private const string InsertNew =
        """
        INSERT INTO market.bars_1m (instrument_id, ts, open, high, low, close, volume, source)
        SELECT @id, s.ts, s.open, s.high, s.low, s.close, s.volume, @source
        FROM stage_bars s
        WHERE NOT EXISTS (SELECT 1 FROM market.bars_1m b WHERE b.instrument_id = @id AND b.ts = s.ts)
        """;

    private const string UpdateExisting =
        """
        UPDATE market.bars_1m b SET open = s.open, high = s.high, low = s.low, close = s.close, volume = s.volume, source = @source
        FROM stage_bars s
        WHERE b.instrument_id = @id AND b.ts = s.ts
          AND ((@source = 'csv' AND b.source = 'csv' AND (b.open, b.high, b.low, b.close, b.volume) IS DISTINCT FROM (s.open, s.high, s.low, s.close, s.volume))
            OR (@source = 'dhan' AND ((b.open, b.high, b.low, b.close, b.volume) IS DISTINCT FROM (s.open, s.high, s.low, s.close, s.volume) OR b.source <> 'dhan')))
        """;

    private const string RecordFile =
        """
        INSERT INTO market.csv_files (file_name, size_bytes, modified_at, instrument_id, rows, inserted, updated, repaired, rejected, loaded_at, first_bar, last_bar, undone_at)
        VALUES (@name, @size, @modified, @id, @rows, @inserted, @updated, @repaired, @rejected, @now, @firstBar, @lastBar, NULL)
        ON CONFLICT (file_name) DO UPDATE SET
            size_bytes = EXCLUDED.size_bytes, modified_at = EXCLUDED.modified_at, instrument_id = EXCLUDED.instrument_id, rows = EXCLUDED.rows,
            inserted = EXCLUDED.inserted, updated = EXCLUDED.updated, repaired = EXCLUDED.repaired, rejected = EXCLUDED.rejected, loaded_at = EXCLUDED.loaded_at,
            first_bar = EXCLUDED.first_bar, last_bar = EXCLUDED.last_bar, undone_at = NULL
        """;

    public async ValueTask<IBarMergeSession> BeginMergeAsync(Guid instrumentId, BarSource source, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var create = new NpgsqlCommand(CreateStage, connection, transaction))
            {
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            var importer = await connection.BeginBinaryImportAsync(CopyStage, cancellationToken);
            return new MergeSession(connection, transaction, importer, instrumentId, source, clock);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<IReadOnlyList<Bar>> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT extract(epoch FROM ts)::bigint, open, high, low, close, volume
            FROM market.bars_1m
            WHERE instrument_id = @id AND ts >= @from AND ts <= @to
            ORDER BY ts
            """);
        command.Parameters.AddWithValue("id", instrumentId);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        var bars = new List<Bar>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            bars.Add(new Bar(reader.GetInt64(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetInt64(5)));
        }

        return bars;
    }

    private sealed class MergeSession(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        NpgsqlBinaryImporter importer,
        Guid instrumentId,
        BarSource source,
        TimeProvider clock) : IBarMergeSession
    {
        private bool _completed;

        public async ValueTask AddAsync(ReadOnlyMemory<Bar> bars, CancellationToken cancellationToken)
        {
            for (var i = 0; i < bars.Length; i++)
            {
                var bar = bars.Span[i];
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(DateTime.UnixEpoch.AddSeconds(bar.EpochSeconds), NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(bar.Open, NpgsqlDbType.Double, cancellationToken);
                await importer.WriteAsync(bar.High, NpgsqlDbType.Double, cancellationToken);
                await importer.WriteAsync(bar.Low, NpgsqlDbType.Double, cancellationToken);
                await importer.WriteAsync(bar.Close, NpgsqlDbType.Double, cancellationToken);
                await importer.WriteAsync(bar.Volume, NpgsqlDbType.Bigint, cancellationToken);
            }
        }

        public async ValueTask<BarMergeResult> CompleteAsync(CsvFileMark? file, CancellationToken cancellationToken)
        {
            await importer.CompleteAsync(cancellationToken);
            await importer.DisposeAsync();
            var now = clock.GetUtcNow();

            await using (var analyze = new NpgsqlCommand("ANALYZE stage_bars", connection, transaction))
            {
                await analyze.ExecuteNonQueryAsync(cancellationToken);
            }

            long inserted, updated, same, kept, taken;
            DateTimeOffset? dirtyFrom = null, dirtyTo = null;
            await using (var classify = Command(Classify))
            await using (var reader = await classify.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                inserted = reader.GetInt64(0);
                updated = reader.GetInt64(1);
                same = reader.GetInt64(2);
                kept = reader.GetInt64(3);
                taken = reader.GetInt64(4);
                if (!reader.IsDBNull(5))
                {
                    dirtyFrom = reader.GetFieldValue<DateTimeOffset>(5);
                    dirtyTo = reader.GetFieldValue<DateTimeOffset>(6);
                }
            }

            if (inserted > 0)
            {
                await using var insert = Command(InsertNew);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            if (updated + taken > 0)
            {
                await using var update = Command(UpdateExisting);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var coverage = Command(BarCoverageSql.Refresh))
            {
                coverage.Parameters.AddWithValue("now", now);
                coverage.Parameters.AddWithValue("dirtyFrom", NpgsqlDbType.TimestampTz, (object?)dirtyFrom ?? DBNull.Value);
                coverage.Parameters.AddWithValue("dirtyTo", NpgsqlDbType.TimestampTz, (object?)dirtyTo ?? DBNull.Value);
                await coverage.ExecuteNonQueryAsync(cancellationToken);
            }

            if (file is not null)
            {
                await using var record = Command(RecordFile);
                record.Parameters.AddWithValue("name", file.Name);
                record.Parameters.AddWithValue("size", file.Size);
                record.Parameters.AddWithValue("modified", file.ModifiedAt);
                record.Parameters.AddWithValue("rows", file.Rows);
                record.Parameters.AddWithValue("inserted", inserted);
                record.Parameters.AddWithValue("updated", updated);
                record.Parameters.AddWithValue("repaired", file.Repaired);
                record.Parameters.AddWithValue("rejected", file.Rejected);
                record.Parameters.AddWithValue("now", now);
                record.Parameters.AddWithValue("firstBar", NpgsqlDbType.TimestampTz, (object?)file.FirstBar ?? DBNull.Value);
                record.Parameters.AddWithValue("lastBar", NpgsqlDbType.TimestampTz, (object?)file.LastBar ?? DBNull.Value);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            _completed = true;
            return new BarMergeResult(inserted, updated, same, kept);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_completed)
            {
                await importer.DisposeAsync();
                await transaction.DisposeAsync(); // not committed: rolled back
            }

            await connection.DisposeAsync();
        }

        private NpgsqlCommand Command(string sql)
        {
            var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = MergeTimeoutSeconds };
            if (sql.Contains("@id", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("id", instrumentId);
            }

            if (sql.Contains("@source", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("source", source.ToCode());
            }

            return command;
        }
    }
}
