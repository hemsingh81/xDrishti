using Npgsql;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <c>filings.filings</c> plus <c>filings.announcements</c> (P15-07). Insert-only for the runtime role. One row is ONE round trip: the envelope insert and the announcement insert go as one batch, which the server runs as a single implicit transaction (both or neither), each
/// <c>ON CONFLICT DO NOTHING</c> so that the same row again is an answer (<c>Duplicate</c>), not an error. An announcement has no period, so it is never grouped (no lock, no revision, no supersede link). The same hash always means the same instrument, time and text (they are in the
/// hashed text), so there is no sidecar conflict to detect. The text is not here: it is the stored file of the envelope.
/// </summary>
internal sealed class AnnouncementStore(NpgsqlDataSource dataSource) : IAnnouncementStore
{
    public async ValueTask<FilingInsertResult> InsertAsync(NewAnnouncement announcement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(announcement);
        var filing = announcement.Filing;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = new NpgsqlBatchCommand("INSERT INTO filings.announcements (filing_id, category, headline) VALUES (@id, @category, @headline) ON CONFLICT (filing_id) DO NOTHING");
        row.Parameters.AddWithValue("id", filing.Id);
        row.Parameters.AddWithValue("category", announcement.Category);
        row.Parameters.AddWithValue("headline", announcement.Headline);
        var written = await RowStoreBatches.TryWriteAsync(connection, null, [RowStoreBatches.Envelope(filing, null), row], cancellationToken);
        return new FilingInsertResult(written == true ? FilingInsertOutcome.Stored : written is null ? FilingInsertOutcome.BadSupersede : FilingInsertOutcome.Duplicate);
    }
}
