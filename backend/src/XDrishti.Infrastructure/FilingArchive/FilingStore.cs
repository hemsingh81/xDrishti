using System.Data;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <c>filings.filings</c> (P15-02, ADR 0180). Insert-only for the runtime role. One insert is one transaction at READ COMMITTED: the group of (instrument, kind, period) is locked with a transaction advisory lock, the stored envelope of the same bytes is looked up
/// (after the lock, so a second host sees the first one's commit), the group's latest filing is read (the group is the FILE envelopes only: a row envelope made from a csv by the P15-04 stores, which has a calendar or shareholding row, is another group, ADR 0181) and <see cref="PointInTimeRule.Decide"/> picks the outcome, then the row is inserted <c>ON CONFLICT (content_sha256) DO NOTHING</c>. Nothing here reads a file.
/// </summary>
internal sealed class FilingStore(NpgsqlDataSource dataSource) : IFilingStore
{
    public async ValueTask<StoredFiling?> FindByHashAsync(string contentSha256, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {FilingRows.Columns} FROM filings.filings WHERE content_sha256 = @h");
        command.Parameters.AddWithValue("h", contentSha256);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? FilingRows.Read(reader) : null;
    }

    public async ValueTask<FilingInsertResult> InsertAsync(NewFiling filing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filing);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var grouped = filing.State == BroadcastState.Ok && filing.PeriodEnd is not null;
        if (grouped)
        {
            await using var @lock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
            @lock.Parameters.AddWithValue("key", GroupLockKey(filing));
            await @lock.ExecuteNonQueryAsync(cancellationToken);
        }

        var existing = await ByHashAsync(connection, transaction, filing.ContentSha256, cancellationToken);
        if (existing is not null)
        {
            return Existing(filing, existing);
        }

        var decision = PointInTimeRule.Decide(filing, grouped ? await GroupAsync(connection, transaction, filing, cancellationToken) : []);
        if (decision.Outcome == RevisionOutcome.AmbiguousOrder)
        {
            return new FilingInsertResult(FilingInsertOutcome.AmbiguousOrder);
        }

        if (decision.Outcome == RevisionOutcome.BadSupersede)
        {
            return new FilingInsertResult(FilingInsertOutcome.BadSupersede);
        }

        var rows = await InsertRowAsync(connection, transaction, filing, decision.Supersedes, cancellationToken);
        if (rows == 0)
        {
            var raced = await ByHashAsync(connection, transaction, filing.ContentSha256, cancellationToken);
            return raced is null ? throw new InvalidOperationException("The envelope was neither inserted nor found.") : Existing(filing, raced);
        }

        await transaction.CommitAsync(cancellationToken);
        return new FilingInsertResult(decision.Outcome == RevisionOutcome.StoreOutOfOrder ? FilingInsertOutcome.StoredOutOfOrder : FilingInsertOutcome.Stored, null, decision.Supersedes);
    }

    /// <summary>The advisory lock key of the (instrument, kind, period) group. Every writer of a group (this store and the calendar and shareholding stores) takes the same key, so they are serialised.</summary>
    internal static string GroupLockKey(NewFiling filing) => $"filings:{filing.InstrumentId:D}:{FilingKinds.Code(filing.Kind)}:{filing.PeriodEnd:yyyy-MM-dd}";

    internal static FilingInsertResult Existing(NewFiling filing, StoredFiling stored) =>
        new(filing.SameSidecar(stored) ? FilingInsertOutcome.Duplicate : FilingInsertOutcome.SidecarConflict, stored);

    internal static async ValueTask<StoredFiling?> ByHashAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string hash, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"SELECT {FilingRows.Columns} FROM filings.filings WHERE content_sha256 = @h", connection, transaction);
        command.Parameters.AddWithValue("h", hash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? FilingRows.Read(reader) : null;
    }

    private static async ValueTask<IReadOnlyCollection<StoredFiling>> GroupAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NewFiling filing, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            SELECT {FilingRows.Qualified("f")} FROM filings.filings f
            WHERE f.instrument_id = @i AND f.kind = @k AND f.period_end = @p AND f.broadcast_state = 'ok'
              AND NOT EXISTS (SELECT 1 FROM filings.calendar c WHERE c.filing_id = f.id) AND NOT EXISTS (SELECT 1 FROM filings.shareholding s WHERE s.filing_id = f.id)
            """, connection, transaction);
        command.Parameters.AddWithValue("i", filing.InstrumentId);
        command.Parameters.AddWithValue("k", FilingKinds.Code(filing.Kind));
        command.Parameters.AddWithValue("p", filing.PeriodEnd!.Value);
        var rows = new List<StoredFiling>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(FilingRows.Read(reader));
        }

        return rows;
    }

    private static async ValueTask<int> InsertRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, NewFiling filing, Guid? supersedes, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO filings.filings (id, instrument_id, kind, period_end, broadcast_ts, broadcast_state, unavailable_reason, ingested_at, source, content_sha256, file_bytes, file_name, supersedes)
            VALUES (@id, @instrument, @kind, @period, @ts, @state, @reason, @ingested, 'folder_drop', @hash, @bytes, @name, @supersedes)
            ON CONFLICT (content_sha256) DO NOTHING
            """, connection, transaction);
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
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
