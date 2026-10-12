using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>
/// Undoes the load of one CSV file in one transaction (ADR 0017 §6). The runtime role has no DELETE on the bars, so the removal is <c>market.undo_csv_bars(instrument, from, to)</c>, a database function that
/// runs with its owner's rights and deletes only <c>csv</c>-source bars of that one instrument within the range (and the aggregates of the buckets they were in, and keeps the coverage and its changed range true);
/// this class finds the file's recorded range, calls it and marks the file undone so it is pending again — all or nothing.
/// </summary>
internal sealed class CsvUndo(NpgsqlDataSource dataSource) : ICsvUndo
{
    /// <summary>Deleting from compressed chunks decompresses and rewrites batches: more than the pool's default command timeout.</summary>
    private const int TimeoutSeconds = 900;

    // The file's own range; a file loaded before the range was recorded falls back to the instrument's whole range (the function still deletes only CSV bars).
    private const string FindFile =
        """
        SELECT f.instrument_id, COALESCE(f.first_bar, c.first_bar), COALESCE(f.last_bar, c.last_bar)
        FROM market.csv_files f
        LEFT JOIN market.bar_coverage c ON c.instrument_id = f.instrument_id
        WHERE f.file_name = @name AND f.undone_at IS NULL
        FOR UPDATE OF f
        """;

    public async ValueTask<CsvUndoResult?> UndoAsync(string fileName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Guid instrumentId;
        DateTimeOffset? from, to;
        await using (var find = new NpgsqlCommand(FindFile, connection, transaction))
        {
            find.Parameters.AddWithValue("name", fileName);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0))
            {
                return null;
            }

            instrumentId = reader.GetGuid(0);
            from = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
            to = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
        }

        var removed = 0L;
        if (from is { } start && to is { } end)
        {
            await using var undo = new NpgsqlCommand("SELECT market.undo_csv_bars(@id, @from, @to)", connection, transaction) { CommandTimeout = TimeoutSeconds };
            undo.Parameters.AddWithValue("id", instrumentId);
            undo.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, start);
            undo.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, end);
            removed = (long)(await undo.ExecuteScalarAsync(cancellationToken))!;
        }

        await using (var mark = new NpgsqlCommand("UPDATE market.csv_files SET undone_at = @now WHERE file_name = @name", connection, transaction))
        {
            mark.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            mark.Parameters.AddWithValue("name", fileName);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new CsvUndoResult(instrumentId, from ?? now, to ?? now, removed);
    }
}
