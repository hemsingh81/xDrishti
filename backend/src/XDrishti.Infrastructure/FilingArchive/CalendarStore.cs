using System.Data;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <c>filings.filings</c> plus <c>filings.calendar</c> (P15-04, ADR 0181). Insert-only for the runtime role. One row is one transaction at READ COMMITTED, on the same lock and the same rule as the envelope store, but the group the rule sees is the stored rows of the same
/// instrument, period AND purpose (an <c>other</c> meeting never supersedes a <c>results</c> one): lock, look up the hash, read the group (one batch), <see cref="PointInTimeRule.Decide"/>, insert the envelope and the calendar row (one batch), commit. Nothing here reads a file.
/// </summary>
internal sealed class CalendarStore(NpgsqlDataSource dataSource) : ICalendarStore
{
    private static readonly string GroupSql =
        $"""
        SELECT {FilingRows.Qualified("f")} FROM filings.filings f JOIN filings.calendar c ON c.filing_id = f.id
        WHERE f.instrument_id = @i AND f.kind = 'board_meeting' AND f.period_end = @p AND f.broadcast_state = 'ok' AND c.purpose = @purpose
        """;

    public async ValueTask<FilingInsertResult> InsertAsync(NewMeeting meeting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        var filing = meeting.Filing;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var read = await RowStoreBatches.ReadAsync(connection, transaction, filing, meeting.Purpose, GroupSql, cancellationToken);
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

        var calendar = new NpgsqlBatchCommand("INSERT INTO filings.calendar (filing_id, meeting_date, purpose, status, period_end) VALUES (@id, @meeting, @purpose, @status, @period) ON CONFLICT (filing_id) DO NOTHING");
        calendar.Parameters.AddWithValue("id", filing.Id);
        calendar.Parameters.Add(new NpgsqlParameter("meeting", NpgsqlDbType.Date) { Value = meeting.MeetingDate });
        calendar.Parameters.AddWithValue("purpose", meeting.Purpose);
        calendar.Parameters.AddWithValue("status", meeting.Status);
        calendar.Parameters.Add(new NpgsqlParameter("period", NpgsqlDbType.Date) { Value = filing.PeriodEnd!.Value });
        var written = await RowStoreBatches.TryWriteAsync(connection, transaction, [RowStoreBatches.Envelope(filing, decision.Supersedes), calendar], cancellationToken);
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
