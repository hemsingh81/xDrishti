using System.Data;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <c>filings.filings</c> plus <c>filings.shareholding</c> (P15-04, ADR 0181). Insert-only for the runtime role. One group is one transaction at READ COMMITTED, on the lock and the rule of the envelope store (a later group of the same instrument and period supersedes the
/// stored one): lock, look up the hash, read the group (one batch), <see cref="PointInTimeRule.Decide"/>, insert the envelope and its lines (one batch), commit. A blank percentage is stored as state <c>unavailable</c> with no number.
/// </summary>
internal sealed class ShareholdingStore(NpgsqlDataSource dataSource) : IShareholdingStore
{
    // Only the row envelopes (the ones with shareholding lines): a file envelope of the same instrument, kind and period is another group (ADR 0181).
    private static readonly string GroupSql =
        $"""
        SELECT {FilingRows.Qualified("f")} FROM filings.filings f
        WHERE f.instrument_id = @i AND f.kind = 'shareholding' AND f.period_end = @p AND f.broadcast_state = 'ok' AND EXISTS (SELECT 1 FROM filings.shareholding s WHERE s.filing_id = f.id)
        """;

    public async ValueTask<FilingInsertResult> InsertAsync(NewShareholding group, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(group);
        var filing = group.Filing;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var read = await RowStoreBatches.ReadAsync(connection, transaction, filing, null, GroupSql, cancellationToken);
        if (read.Existing is not null)
        {
            return FilingStore.Existing(filing, read.Existing);
        }

        var decision = PointInTimeRule.Decide(filing, read.Group);
        if (decision.Outcome == RevisionOutcome.AmbiguousOrder)
        {
            return new FilingInsertResult(FilingInsertOutcome.AmbiguousOrder);
        }

        if (decision.Outcome == RevisionOutcome.BadSupersede)
        {
            return new FilingInsertResult(FilingInsertOutcome.BadSupersede);
        }

        var commands = new List<NpgsqlBatchCommand> { RowStoreBatches.Envelope(filing, decision.Supersedes) };
        foreach (var line in group.Lines)
        {
            var insert = new NpgsqlBatchCommand("INSERT INTO filings.shareholding (filing_id, category, pct, pledged_pct, state) VALUES (@id, @category, @pct, @pledged, @state) ON CONFLICT (filing_id, category) DO NOTHING");
            insert.Parameters.AddWithValue("id", filing.Id);
            insert.Parameters.AddWithValue("category", line.Category);
            insert.Parameters.Add(new NpgsqlParameter("pct", NpgsqlDbType.Numeric) { Value = line.Pct is { } p ? p : DBNull.Value });
            insert.Parameters.Add(new NpgsqlParameter("pledged", NpgsqlDbType.Numeric) { Value = line.PledgedPct is { } l ? l : DBNull.Value });
            insert.Parameters.AddWithValue("state", line.IsValue ? "value" : "unavailable");
            commands.Add(insert);
        }

        var written = await RowStoreBatches.TryWriteAsync(connection, transaction, commands, cancellationToken);
        if (written is null)
        {
            return new FilingInsertResult(FilingInsertOutcome.BadSupersede);
        }

        if (!written.Value)
        {
            var raced = await FilingStore.ByHashAsync(connection, transaction, filing.ContentSha256, cancellationToken);
            return raced is null ? throw new InvalidOperationException("The envelope was neither inserted nor found.") : FilingStore.Existing(filing, raced);
        }

        await transaction.CommitAsync(cancellationToken);
        return new FilingInsertResult(decision.Outcome == RevisionOutcome.StoreOutOfOrder ? FilingInsertOutcome.StoredOutOfOrder : FilingInsertOutcome.Stored, null, decision.Supersedes);
    }
}
