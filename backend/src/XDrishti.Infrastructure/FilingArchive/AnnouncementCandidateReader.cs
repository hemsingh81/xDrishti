using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// The stored announcements for the sample (P15-07), read only: the dated ones that have a <c>filings.announcements</c> row (state ok, in a time range) with symbol and category, the count of announcement envelopes that have no row (a text or pdf file), and the stored text of
/// chosen filings through the files store (hash checked on open). Nothing is written.
/// </summary>
internal sealed class AnnouncementCandidateReader(NpgsqlDataSource dataSource, IFilingFiles files) : IAnnouncementCandidates
{
    private const string Exchange = "NSE:";

    public async ValueTask<AnnouncementCandidates> ListAsync(DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        var dated = new List<SampleCandidate>();
        await using (var command = dataSource.CreateCommand(
            """
            SELECT f.id, i.canonical_symbol, f.broadcast_ts, a.category
            FROM filings.filings f
            JOIN filings.announcements a ON a.filing_id = f.id
            JOIN market.instruments i ON i.id = f.instrument_id
            WHERE f.kind = 'announcement' AND f.broadcast_state = 'ok' AND f.broadcast_ts >= @from AND f.broadcast_ts < @to AND a.category IS NOT NULL
            """))
        {
            Range(command, from, toExclusive);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var symbol = reader.GetString(1);
                dated.Add(new SampleCandidate(reader.GetGuid(0), symbol.StartsWith(Exchange, StringComparison.Ordinal) ? symbol[Exchange.Length..] : symbol, new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero), reader.GetString(3)));
            }
        }

        int noMetadata;
        await using (var command = dataSource.CreateCommand(
            """
            SELECT count(*) FROM filings.filings f
            WHERE f.kind = 'announcement' AND f.broadcast_state = 'ok' AND f.broadcast_ts >= @from AND f.broadcast_ts < @to
              AND NOT EXISTS (SELECT 1 FROM filings.announcements a WHERE a.filing_id = f.id)
            """))
        {
            Range(command, from, toExclusive);
            noMetadata = (int)Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }

        return new AnnouncementCandidates(dated, noMetadata);
    }

    public async ValueTask<IReadOnlyDictionary<Guid, AnnouncementBody>> ReadBodiesAsync(IReadOnlyCollection<Guid> filingIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filingIds);
        var hashes = new Dictionary<Guid, string>();
        await using (var command = dataSource.CreateCommand("SELECT id, content_sha256 FROM filings.filings WHERE kind = 'announcement' AND id = ANY(@ids)"))
        {
            command.Parameters.AddWithValue("ids", filingIds.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                hashes[reader.GetGuid(0)] = reader.GetString(1);
            }
        }

        var bodies = new Dictionary<Guid, AnnouncementBody>();
        foreach (var (id, sha) in hashes)
        {
            var opened = await files.OpenAsync(sha, cancellationToken);
            if (opened.IsFailure)
            {
                continue;
            }

            await using var stream = opened.Value;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            if (AnnouncementBody.Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)) is { } body)
            {
                bodies[id] = body;
            }
        }

        return bodies;
    }

    private static void Range(NpgsqlCommand command, DateTimeOffset from, DateTimeOffset to)
    {
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = from.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = to.UtcDateTime });
    }
}
