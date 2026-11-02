using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>
/// <see cref="IResultsFilingTimes"/> (P15-06): the instrument, period and broadcast time of the stored <c>results</c> filings with a known time, for the given instruments, through the index <c>(instrument_id, kind, broadcast_ts)</c>. Envelope columns only, SELECT only; the visible
/// time is the broadcast time, never the ingest time or a file name.
/// </summary>
internal sealed class ResultsFilingTimes(NpgsqlDataSource dataSource) : IResultsFilingTimes
{
    public async ValueTask<IReadOnlyList<ResultsFilingTime>> ReadAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        await using var command = dataSource.CreateCommand(
            "SELECT instrument_id, period_end, broadcast_ts FROM filings.filings WHERE kind = 'results' AND broadcast_state = 'ok' AND broadcast_ts IS NOT NULL AND instrument_id = ANY(@ids) ORDER BY instrument_id, broadcast_ts, id");
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = instrumentIds.ToArray() });
        var rows = new List<ResultsFilingTime>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ResultsFilingTime(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1), new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero)));
        }

        return rows;
    }
}
