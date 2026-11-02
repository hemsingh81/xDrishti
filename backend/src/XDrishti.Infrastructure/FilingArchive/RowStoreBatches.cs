using Npgsql;
using NpgsqlTypes;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// The shared steps of the calendar and shareholding stores (P15-04), sent as batches so that one row costs four round trips (begin, read, write, commit): the read batch takes the group lock, looks up the hash and reads the group; the write batch inserts the envelope and the
/// fact rows, all <c>ON CONFLICT DO NOTHING</c> so that a lost race is an answer, not an error.
/// </summary>
internal static class RowStoreBatches
{
    public sealed record GroupRead(StoredFiling? Existing, IReadOnlyCollection<StoredFiling> Group);

    /// <summary>Locks the (instrument, kind, period) group when it has one, looks up the envelope of the same bytes and reads the group's stored envelopes selected by <paramref name="groupSql"/> (a SELECT of <see cref="FilingRows.Qualified"/> columns with the parameters @i, @p and @purpose).</summary>
    public static async ValueTask<GroupRead> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NewFiling filing, string? purpose, string groupSql, CancellationToken cancellationToken)
    {
        var grouped = filing.State == BroadcastState.Ok && filing.PeriodEnd is not null;
        await using var batch = new NpgsqlBatch(connection, transaction);
        if (grouped)
        {
            var @lock = new NpgsqlBatchCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))");
            @lock.Parameters.AddWithValue("key", FilingStore.GroupLockKey(filing));
            batch.BatchCommands.Add(@lock);
        }

        var byHash = new NpgsqlBatchCommand($"SELECT {FilingRows.Columns} FROM filings.filings WHERE content_sha256 = @h");
        byHash.Parameters.AddWithValue("h", filing.ContentSha256);
        batch.BatchCommands.Add(byHash);
        if (grouped)
        {
            var group = new NpgsqlBatchCommand(groupSql);
            group.Parameters.AddWithValue("i", filing.InstrumentId);
            group.Parameters.Add(new NpgsqlParameter("p", NpgsqlDbType.Date) { Value = filing.PeriodEnd!.Value });
            if (purpose is not null)
            {
                group.Parameters.AddWithValue("purpose", purpose);
            }

            batch.BatchCommands.Add(group);
        }

        await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
        if (grouped)
        {
            await reader.NextResultAsync(cancellationToken);
        }

        StoredFiling? existing = await reader.ReadAsync(cancellationToken) ? FilingRows.Read(reader) : null;
        var rows = new List<StoredFiling>();
        if (grouped && await reader.NextResultAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(FilingRows.Read(reader));
            }
        }

        return new GroupRead(existing, rows);
    }

    /// <summary>The envelope insert as a batch command (<c>ON CONFLICT (content_sha256) DO NOTHING</c>).</summary>
    public static NpgsqlBatchCommand Envelope(NewFiling filing, Guid? supersedes)
    {
        var command = new NpgsqlBatchCommand(
            """
            INSERT INTO filings.filings (id, instrument_id, kind, period_end, broadcast_ts, broadcast_state, unavailable_reason, ingested_at, source, content_sha256, file_bytes, file_name, supersedes)
            VALUES (@id, @instrument, @kind, @period, @ts, @state, @reason, @ingested, 'folder_drop', @hash, @bytes, @name, @supersedes)
            ON CONFLICT (content_sha256) DO NOTHING
            """);
        command.Parameters.AddWithValue("id", filing.Id);
        command.Parameters.AddWithValue("instrument", filing.InstrumentId);
        command.Parameters.AddWithValue("kind", FilingKinds.Code(filing.Kind));
        command.Parameters.Add(new NpgsqlParameter("period", NpgsqlDbType.Date) { Value = filing.PeriodEnd is { } p ? p : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("ts", NpgsqlDbType.TimestampTz) { Value = filing.BroadcastTs is { } t ? t.UtcDateTime : DBNull.Value });
        command.Parameters.AddWithValue("state", BroadcastTime.Code(filing.State));
        command.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Varchar) { Value = (object?)filing.UnavailableReason ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("ingested", NpgsqlDbType.TimestampTz) { Value = filing.IngestedAt.UtcDateTime });
        command.Parameters.AddWithValue("hash", filing.ContentSha256);
        command.Parameters.AddWithValue("bytes", filing.FileBytes);
        command.Parameters.AddWithValue("name", filing.FileName);
        command.Parameters.Add(new NpgsqlParameter("supersedes", NpgsqlDbType.Uuid) { Value = supersedes is { } s ? s : DBNull.Value });
        return command;
    }

    /// <summary>
    /// Runs the write batch: true when the envelope was inserted, false when another writer stored the same bytes first, null when the database refused the revision (a second revision of one filing, <c>ux_filings_supersedes</c>): a refusal is an answer
    /// (<c>bad_supersede</c>), not an exception that would stop the rest of the file.
    /// </summary>
    public static async ValueTask<bool?> TryWriteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<NpgsqlBatchCommand> commands, CancellationToken cancellationToken)
    {
        await using var batch = new NpgsqlBatch(connection, transaction);
        foreach (var command in commands)
        {
            batch.BatchCommands.Add(command);
        }

        try
        {
            await batch.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException failure) when (failure.SqlState == PostgresErrorCodes.UniqueViolation && failure.ConstraintName == "ux_filings_supersedes")
        {
            return null;
        }

        return batch.BatchCommands[0].Rows > 0;
    }
}
